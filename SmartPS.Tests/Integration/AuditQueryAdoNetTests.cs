using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace SmartPS.Tests.Integration;

/// <summary>
/// DA-4: the audit screen query is written with plain ADO.NET (SqlConnection / SqlCommand / SqlDataReader).
/// These tests prove it returns exactly what the previous EF Core query returned, and that user input
/// only ever travels as SQL parameters.
/// </summary>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Integration")]
public class AuditQueryAdoNetTests : IClassFixture<SqlServerDatabaseFixture>
{
    private readonly SqlServerDatabaseFixture _db;

    public AuditQueryAdoNetTests(SqlServerDatabaseFixture db)
    {
        _db = db;
    }

    private static DateOnly TodayVn() => DateOnly.FromDateTime(AuditTime.ToVietnamTime(DateTime.UtcNow));

    private async Task<ServiceProvider> ManagerSessionAsync()
    {
        var manager = await TestUsers.CreateAsync(_db.Factory, "Manager");
        var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(manager.Username);
        return sp;
    }

    private async Task<string> SeedAsync()
    {
        var username = TestUsers.UniqueName("ado");
        var writer = new AuditService(_db.Factory, new CurrentUserContext());
        var actor = new AuditActor(null, username, "Operator");

        var rows = new (string Action, AuditOutcome Outcome, string? EntityId, object? Details)[]
        {
            (AuditActions.AuthLoginSuccess, AuditOutcome.Success, null, null),
            (AuditActions.AuthLogout, AuditOutcome.Success, "ticket-100%", new { Plate = "51A12345" }),
            (AuditActions.AuthLogout, AuditOutcome.Failed, "a_b[1]", new { Note = "Biển số xe" }),
            (AuditActions.AuthLoginSuccess, AuditOutcome.Denied, "back\\slash", new { Note = "O'Brien; DROP TABLE" }),
            (AuditActions.AuthLogout, AuditOutcome.Success, "plain", new { Plate = "30G99999" }),
        };

        foreach (var row in rows)
        {
            Assert.True(await writer.LogAsync(new AuditEntry(row.Action, row.Outcome, "T", row.EntityId, row.Details, actor)));
        }

        return username;
    }

    /// <summary>The previous EF Core implementation, kept here only as the reference the ADO.NET version must match.</summary>
    private async Task<(int Total, List<AuditLog> Items)> EfReferenceAsync(AuditQueryFilter filter)
    {
        var pageSize = Math.Clamp(filter.PageSize, 1, AuditQueryFilter.MaxPageSize);
        var pageIndex = Math.Max(0, filter.PageIndex);
        await using var db = await _db.Factory.CreateDbContextAsync();

        IQueryable<AuditLog> query;
        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var pattern = "%" + EscapeLike(filter.SearchText.Trim()) + "%";
            query = db.AuditLogs
                .FromSqlInterpolated($"""
                    SELECT * FROM "AuditLogs"
                    WHERE "EntityId" COLLATE Vietnamese_100_CI_AS LIKE {pattern} ESCAPE '\'
                       OR "Details" COLLATE Vietnamese_100_CI_AS LIKE {pattern} ESCAPE '\'
                    """)
                .AsNoTracking();
        }
        else
        {
            query = db.AuditLogs.AsNoTracking();
        }

        if (filter.FromDateVn.HasValue)
        {
            var from = AuditTime.VietnamDateStartUtc(filter.FromDateVn.Value);
            query = query.Where(a => a.OccurredAtUtc >= from);
        }

        if (filter.ToDateVn.HasValue)
        {
            var to = AuditTime.VietnamDateStartUtc(filter.ToDateVn.Value.AddDays(1));
            query = query.Where(a => a.OccurredAtUtc < to);
        }

        if (!string.IsNullOrWhiteSpace(filter.Username))
        {
            var userTerm = filter.Username.Trim().ToLowerInvariant();
            query = query.Where(a => a.Username.ToLower().Contains(userTerm));
        }

        if (!string.IsNullOrWhiteSpace(filter.Action))
        {
            var action = filter.Action.Trim();
            query = query.Where(a => a.Action == action);
        }

        if (filter.Outcome.HasValue)
        {
            var outcome = filter.Outcome.Value;
            query = query.Where(a => a.Outcome == outcome);
        }

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(a => a.AuditLogId).Skip(pageIndex * pageSize).Take(pageSize).ToListAsync();
        return (total, items);
    }

    private static string EscapeLike(string term)
    {
        var builder = new StringBuilder();
        foreach (var ch in term)
        {
            if (ch is '\\' or '%' or '_' or '[')
            {
                builder.Append('\\');
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    private static void AssertSameRecord(AuditLog expected, AuditLog actual)
    {
        Assert.Equal(expected.AuditLogId, actual.AuditLogId);
        Assert.Equal(expected.OccurredAtUtc, actual.OccurredAtUtc);
        Assert.Equal(DateTimeKind.Utc, actual.OccurredAtUtc.Kind);
        Assert.Equal(expected.UserId, actual.UserId);
        Assert.Equal(expected.Username, actual.Username);
        Assert.Equal(expected.RoleName, actual.RoleName);
        Assert.Equal(expected.Action, actual.Action);
        Assert.Equal(expected.EntityType, actual.EntityType);
        Assert.Equal(expected.EntityId, actual.EntityId);
        Assert.Equal(expected.Outcome, actual.Outcome);
        Assert.Equal(expected.Details, actual.Details);
        Assert.Equal(expected.MachineName, actual.MachineName);
        Assert.Equal(expected.PrevHash, actual.PrevHash);
        Assert.Equal(expected.Hash, actual.Hash);
    }

    public static TheoryData<string> FilterCases => new()
    {
        "none", "action", "outcome", "user", "user-upper", "search-percent", "search-underscore", "search-bracket",
        "search-backslash", "search-details", "search-accent", "search-quote", "combined", "page2", "dates", "no-match"
    };

    [Theory]
    [MemberData(nameof(FilterCases))]
    public async Task Ado_net_query_matches_the_ef_core_reference(string name)
    {
        _db.RequireAvailable();
        var username = await SeedAsync();
        var today = TodayVn();
        var scoped = new AuditQueryFilter { Username = username };

        var filter = name switch
        {
            "none" => scoped,
            "action" => scoped with { Action = AuditActions.AuthLogout },
            "outcome" => scoped with { Outcome = AuditOutcome.Success },
            "user" => new AuditQueryFilter { Username = username.Substring(2, 6) },
            "user-upper" => new AuditQueryFilter { Username = username.ToUpperInvariant() },
            "search-percent" => scoped with { SearchText = "100%" },
            "search-underscore" => scoped with { SearchText = "a_b" },
            "search-bracket" => scoped with { SearchText = "[1]" },
            "search-backslash" => scoped with { SearchText = "back\\slash" },
            "search-details" => scoped with { SearchText = "30G99999" },
            "search-accent" => scoped with { SearchText = "Biển số" },
            "search-quote" => scoped with { SearchText = "O'Brien" },
            "combined" => scoped with { Action = AuditActions.AuthLoginSuccess, Outcome = AuditOutcome.Denied, FromDateVn = today, ToDateVn = today },
            "page2" => scoped with { PageSize = 2, PageIndex = 1 },
            "dates" => scoped with { FromDateVn = today.AddDays(1) },
            _ => scoped with { SearchText = "this-text-matches-nothing" }
        };

        using var sp = await ManagerSessionAsync();
        var actual = await sp.GetRequiredService<IAuditQueryService>().QueryAsync(filter);
        var (expectedTotal, expectedItems) = await EfReferenceAsync(filter);

        Assert.Equal(expectedTotal, actual.TotalCount);
        Assert.Equal(expectedItems.Count, actual.Items.Count);
        for (var i = 0; i < expectedItems.Count; i++)
        {
            AssertSameRecord(expectedItems[i], actual.Items[i]);
        }

        if (name == "search-percent" || name == "search-underscore" || name == "search-bracket" || name == "search-backslash"
            || name == "search-details" || name == "search-accent" || name == "search-quote")
        {
            Assert.Equal(1, actual.TotalCount); // wildcards are literal text, not patterns
        }
    }

    [Fact]
    public async Task User_input_is_a_parameter_never_part_of_the_sql_text()
    {
        _db.RequireAvailable();
        await SeedAsync();
        using var sp = await ManagerSessionAsync();
        var query = sp.GetRequiredService<IAuditQueryService>();

        var attacks = new[] { "'; DROP TABLE AuditLogs; --", "' OR 1=1 --", "%' OR '1'='1" };
        foreach (var attack in attacks)
        {
            var bySearch = await query.QueryAsync(new AuditQueryFilter { SearchText = attack });
            var byUser = await query.QueryAsync(new AuditQueryFilter { Username = attack });
            var byAction = await query.QueryAsync(new AuditQueryFilter { Action = attack });
            Assert.Equal(0, bySearch.TotalCount);
            Assert.Equal(0, byUser.TotalCount);
            Assert.Equal(0, byAction.TotalCount);
        }

        await using var db = await _db.Factory.CreateDbContextAsync();
        Assert.True(await db.AuditLogs.AnyAsync(), "the audit table must still exist and still hold rows");
    }

    [Fact]
    public async Task Page_size_is_clamped_and_negative_page_index_is_zero()
    {
        _db.RequireAvailable();
        var username = await SeedAsync();
        using var sp = await ManagerSessionAsync();
        var query = sp.GetRequiredService<IAuditQueryService>();

        var huge = await query.QueryAsync(new AuditQueryFilter { Username = username, PageSize = 100000 });
        var zero = await query.QueryAsync(new AuditQueryFilter { Username = username, PageSize = 0, PageIndex = -5 });

        Assert.Equal(AuditQueryFilter.MaxPageSize, huge.PageSize);
        Assert.Equal(1, zero.PageSize);
        Assert.Equal(0, zero.PageIndex);
        Assert.Single(zero.Items);
    }

    [Fact]
    public async Task A_caller_without_audit_view_is_still_denied_before_any_sql_runs()
    {
        _db.RequireAvailable();
        var operatorUser = await TestUsers.CreateAsync(_db.Factory, "Operator"); // Operator has no Audit.View
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(operatorUser.Username);

        await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            sp.GetRequiredService<IAuditQueryService>().QueryAsync(new AuditQueryFilter()));
    }

    [Fact]
    public void Audit_query_service_source_uses_plain_ado_net_with_parameters_and_documents_the_ef_core_equivalent()
    {
        var path = Path.Combine(RepoPaths.Services, "Audit", "AuditQueryService.cs");
        var source = File.ReadAllText(path);

        Assert.Contains("SqlConnection", source);
        Assert.Contains("SqlCommand", source);
        Assert.Contains("SqlDataReader", source);
        Assert.Contains("Parameters.Add", source);
        Assert.Contains("ADO.NET", source);

        // The EF Core equivalent lives in a comment block after this marker, so the project can be defended by comparison.
        const string marker = "=== EF CORE EQUIVALENT ===";
        Assert.Contains(marker, source);
        var code = source.Split(marker).First();
        Assert.DoesNotContain(".AuditLogs", code);
        Assert.DoesNotContain("FromSql", code);
        Assert.DoesNotContain("IQueryable", code);
        Assert.DoesNotContain(".ToListAsync", code);

        // No EF Core at all in the real code: not even a DbContext to read the connection string.
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", code);
        Assert.DoesNotContain("DbContext", code);
        Assert.Contains("DatabaseConnectionInfo", code);

        // SQL text is a constant: no user value is concatenated or interpolated into it.
        Assert.DoesNotContain("$\"\"\"", code);
        Assert.DoesNotContain("string.Format", code);
    }
}
