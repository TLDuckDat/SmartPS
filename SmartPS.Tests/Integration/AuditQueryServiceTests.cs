using Microsoft.Extensions.DependencyInjection;

namespace SmartPS.Tests.Integration;

/// <summary>R16 / AC-14: audit log query (Vietnam-date filters, user/action/outcome, search in EntityId and Details, 50 per page, Audit.View).</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class AuditQueryServiceTests : IClassFixture<PostgresDatabaseFixture>
{
    private readonly PostgresDatabaseFixture _db;

    public AuditQueryServiceTests(PostgresDatabaseFixture db)
    {
        _db = db;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static DateOnly TodayVn() => DateOnly.FromDateTime(AuditTime.ToVietnamTime(DateTime.UtcNow));

    private async Task<ServiceProvider> ManagerSessionAsync()
    {
        var manager = await TestUsers.CreateAsync(_db.Factory, "Manager"); // Manager has Audit.View (spec §6.1)
        var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(manager.Username);
        return sp;
    }

    private static IAuditQueryService Query(IServiceProvider sp) => sp.GetRequiredService<IAuditQueryService>();

    [Fact]
    public async Task AC14_today_vn_and_action_filter_pages_by_50_newest_first()
    {
        // AC-14: Given Audit.View, When filtering today (VN) + AUTH_LOGIN_SUCCESS, Then only matching rows, 50 per page.
        _db.RequireAvailable();
        var username = TestUsers.UniqueName("q");
        var context = new CurrentUserContext();
        var writer = new AuditService(_db.Factory, context);
        for (var i = 0; i < 55; i++)
        {
            Assert.True(await writer.LogAsync(new AuditEntry(AuditActions.AuthLoginSuccess, AuditOutcome.Success, null, null, null,
                new AuditActor(null, username, "Operator"))));
        }

        Assert.True(await writer.LogAsync(new AuditEntry(AuditActions.AuthLogout, AuditOutcome.Success, null, null, null,
            new AuditActor(null, username, "Operator"))));

        using var sp = await ManagerSessionAsync();
        var today = TodayVn();
        var filter = new AuditQueryFilter { FromDateVn = today, ToDateVn = today, Action = AuditActions.AuthLoginSuccess, Username = username };

        var page0 = await Query(sp).QueryAsync(filter);
        var page1 = await Query(sp).QueryAsync(filter with { PageIndex = 1 });

        Assert.Equal(55, page0.TotalCount);
        Assert.Equal(2, page0.TotalPages);
        Assert.Equal(50, page0.PageSize);
        Assert.Equal(50, page0.Items.Count);
        Assert.Equal(5, page1.Items.Count);
        Assert.Equal(1, page1.PageIndex);
        var all = page0.Items.Concat(page1.Items).ToList();
        Assert.All(all, r =>
        {
            Assert.Equal(AuditActions.AuthLoginSuccess, r.Action);
            Assert.Equal(username, r.Username);
            Assert.Equal(today, DateOnly.FromDateTime(AuditTime.ToVietnamTime(r.OccurredAtUtc)));
        });
        Assert.Equal(all.Select(r => r.AuditLogId).OrderByDescending(id => id), all.Select(r => r.AuditLogId));
        Assert.Equal(55, all.Select(r => r.AuditLogId).Distinct().Count());

        // Without the user filter the page is still capped at 50 and only the action matches.
        var unfiltered = await Query(sp).QueryAsync(new AuditQueryFilter { FromDateVn = today, ToDateVn = today, Action = AuditActions.AuthLoginSuccess });
        Assert.True(unfiltered.TotalCount >= 55);
        Assert.Equal(50, unfiltered.Items.Count);
        Assert.All(unfiltered.Items, r => Assert.Equal(AuditActions.AuthLoginSuccess, r.Action));
    }

    [Fact]
    public async Task Vietnam_date_boundaries_are_applied_in_utc_plus_7()
    {
        _db.RequireAvailable();
        var username = TestUsers.UniqueName("vn");
        var clock = new FixedTimeProvider();
        var writer = new AuditService(_db.Factory, new CurrentUserContext(), clock);
        var actor = new AuditActor(null, username, "Operator");

        clock.Now = new DateTimeOffset(2026, 10, 6, 16, 30, 0, TimeSpan.Zero); // 2026-10-06 23:30 VN
        Assert.True(await writer.LogAsync(new AuditEntry(AuditActions.AuthLogout, AuditOutcome.Success, "T", "late", null, actor)));
        clock.Now = new DateTimeOffset(2026, 10, 6, 17, 30, 0, TimeSpan.Zero); // 2026-10-07 00:30 VN
        Assert.True(await writer.LogAsync(new AuditEntry(AuditActions.AuthLogout, AuditOutcome.Success, "T", "early", null, actor)));

        using var sp = await ManagerSessionAsync();
        var oct7 = await Query(sp).QueryAsync(new AuditQueryFilter { FromDateVn = new DateOnly(2026, 10, 7), ToDateVn = new DateOnly(2026, 10, 7), Username = username });
        var oct6 = await Query(sp).QueryAsync(new AuditQueryFilter { FromDateVn = new DateOnly(2026, 10, 6), ToDateVn = new DateOnly(2026, 10, 6), Username = username });
        var fromOct6 = await Query(sp).QueryAsync(new AuditQueryFilter { FromDateVn = new DateOnly(2026, 10, 6), Username = username });

        Assert.Equal("early", Assert.Single(oct7.Items).EntityId);
        Assert.Equal("late", Assert.Single(oct6.Items).EntityId);
        Assert.Equal(2, fromOct6.TotalCount);
    }

    [Fact]
    public async Task Outcome_and_user_filters_combine()
    {
        _db.RequireAvailable();
        var username = TestUsers.UniqueName("oc");
        var writer = new AuditService(_db.Factory, new CurrentUserContext());
        var actor = new AuditActor(null, username, "Operator");
        Assert.True(await writer.LogAsync(new AuditEntry(AuditActions.AccessDenied, AuditOutcome.Denied, "Navigation", "Settings", null, actor)));
        Assert.True(await writer.LogAsync(new AuditEntry(AuditActions.AuthLoginFailed, AuditOutcome.Failed, null, null, null, actor)));
        Assert.True(await writer.LogAsync(new AuditEntry(AuditActions.AuthLoginSuccess, AuditOutcome.Success, null, null, null, actor)));

        using var sp = await ManagerSessionAsync();
        var denied = await Query(sp).QueryAsync(new AuditQueryFilter { Username = username, Outcome = AuditOutcome.Denied });

        var row = Assert.Single(denied.Items);
        Assert.Equal(AuditActions.AccessDenied, row.Action);
        Assert.Equal(3, (await Query(sp).QueryAsync(new AuditQueryFilter { Username = username })).TotalCount);
    }

    [Fact]
    public async Task Search_matches_entity_id_and_text_inside_details_case_insensitively()
    {
        _db.RequireAvailable();
        var tag = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var writer = new AuditService(_db.Factory, new CurrentUserContext());
        Assert.True(await writer.LogAsync(new AuditEntry(AuditActions.ParkingCheckOut, AuditOutcome.Success, "ParkingSession", $"S-{tag}",
            new { licensePlate = "51A-000.00" })));
        Assert.True(await writer.LogAsync(new AuditEntry(AuditActions.ParkingCheckIn, AuditOutcome.Success, "ParkingSession", "999999",
            new { licensePlate = $"30K-{tag}" })));

        using var sp = await ManagerSessionAsync();
        var byEntity = await Query(sp).QueryAsync(new AuditQueryFilter { SearchText = $"S-{tag}" });
        var byPlate = await Query(sp).QueryAsync(new AuditQueryFilter { SearchText = $"30k-{tag.ToLowerInvariant()}" });
        var byTag = await Query(sp).QueryAsync(new AuditQueryFilter { SearchText = tag });

        Assert.Equal(AuditActions.ParkingCheckOut, Assert.Single(byEntity.Items).Action);
        Assert.Equal(AuditActions.ParkingCheckIn, Assert.Single(byPlate.Items).Action);
        Assert.Equal(2, byTag.TotalCount);
    }

    [Fact]
    public async Task Search_escapes_like_wildcards()
    {
        _db.RequireAvailable();
        var tag = Guid.NewGuid().ToString("N")[..8];
        var writer = new AuditService(_db.Factory, new CurrentUserContext());
        Assert.True(await writer.LogAsync(new AuditEntry(AuditActions.AuthLogout, AuditOutcome.Success, "T", $"x{tag}%_1")));
        Assert.True(await writer.LogAsync(new AuditEntry(AuditActions.AuthLogout, AuditOutcome.Success, "T", $"x{tag}AB1")));

        using var sp = await ManagerSessionAsync();
        var result = await Query(sp).QueryAsync(new AuditQueryFilter { SearchText = $"{tag}%_" });

        Assert.Equal($"x{tag}%_1", Assert.Single(result.Items).EntityId);
    }

    [Theory]
    [InlineData(1000, 200)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(20, 20)]
    public async Task Page_size_is_clamped_between_1_and_200(int requested, int effective)
    {
        _db.RequireAvailable();
        using var sp = await ManagerSessionAsync();

        var page = await Query(sp).QueryAsync(new AuditQueryFilter { PageSize = requested });

        Assert.Equal(effective, page.PageSize);
        Assert.True(page.Items.Count <= effective);
    }

    [Fact]
    public async Task AC14_user_without_Audit_View_is_denied()
    {
        _db.RequireAvailable();
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(op.Username);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var ex = await Assert.ThrowsAsync<PermissionDeniedException>(() => Query(sp).QueryAsync(new AuditQueryFilter()));

        Assert.Contains(Permissions.AuditView, ex.RequiredPermissions);
        var denied = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore));
        Assert.Equal(AuditActions.AccessDenied, denied.Action);
        Assert.Equal(op.UserId, denied.UserId);
    }
}
