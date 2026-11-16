using System.Data;
using System.Text;
using Microsoft.Data.SqlClient;
using SmartPS.Constants;
using SmartPS.Data;
using SmartPS.Models.Audit;
using SmartPS.Services.Authorization;

namespace SmartPS.Services.Audit;

/// <summary>
/// Truy vấn màn Nhật ký bằng ADO.NET thuần (DA-4): SqlConnection + SqlCommand + SqlDataReader, không dùng LINQ của EF Core.
///
/// Điểm cần nhớ khi bảo vệ (so với bản EF Core ở cuối tệp):
///  - Chuỗi SQL do mình tự viết và tự ghép điều kiện lọc; giá trị người dùng nhập KHÔNG bao giờ nằm trong chuỗi SQL,
///    chỉ đi qua SqlParameter (chống SQL injection).
///  - Tự đọc từng cột bằng SqlDataReader rồi tự dựng AuditLog (EF Core làm việc này bằng change tracker + value converter).
///    Vì vậy phải tự làm hai việc mà EF Core làm ngầm: gắn DateTimeKind.Utc cho cột datetime2 và đổi chuỗi Outcome sang enum.
///  - Phân trang bằng OFFSET ... FETCH NEXT (EF Core sinh đúng câu này cho Skip/Take); đếm tổng bằng một lệnh COUNT(*) riêng.
///  - Chỉ đọc: không có UPDATE/DELETE (bảng còn có trigger chặn sửa/xoá, xem AuditLogConfiguration).
/// Chuỗi kết nối nhận từ DatabaseConnectionInfo (đăng ký trong DI), nên lớp này không tạo context nào của EF Core.
/// EF Core vẫn được dùng ở những chỗ khác của hệ thống (ghi nhật ký, xác minh chuỗi băm, các màn khác).
/// </summary>
public sealed class AuditQueryService : IAuditQueryService
{
    private const string Columns =
        "[AuditLogId], [OccurredAtUtc], [UserId], [Username], [RoleName], [Action], [EntityType], [EntityId], " +
        "[Outcome], [Details], [MachineName], [PrevHash], [Hash]";

    private readonly string _connectionString;
    private readonly IAuthorizationGuard _guard;

    public AuditQueryService(DatabaseConnectionInfo connectionInfo, IAuthorizationGuard guard)
    {
        ArgumentNullException.ThrowIfNull(connectionInfo);
        _connectionString = connectionInfo.ConnectionString;
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
    }

    public async Task<AuditPage> QueryAsync(AuditQueryFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await _guard.DemandAsync(Permissions.AuditView, "AuditLog", null, cancellationToken);

        var pageSize = Math.Clamp(filter.PageSize, 1, AuditQueryFilter.MaxPageSize);
        var pageIndex = Math.Max(0, filter.PageIndex);

        // 1. Ghép điều kiện WHERE: mỗi điều kiện là một đoạn SQL cố định + một tham số.
        var conditions = new List<string>();
        var parameters = new List<SqlParameter>();

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            // COLLATE Vietnamese_100_CI_AS: không phân biệt hoa/thường nhưng phân biệt dấu; ESCAPE '\' để % _ [ nhập vào là chữ thường.
            conditions.Add(
                "([EntityId] COLLATE Vietnamese_100_CI_AS LIKE @search ESCAPE '\\' " +
                "OR [Details] COLLATE Vietnamese_100_CI_AS LIKE @search ESCAPE '\\')");
            parameters.Add(NVarChar("@search", "%" + EscapeLike(filter.SearchText.Trim()) + "%"));
        }

        if (filter.FromDateVn.HasValue)
        {
            conditions.Add("[OccurredAtUtc] >= @from");
            parameters.Add(DateTime2("@from", AuditTime.VietnamDateStartUtc(filter.FromDateVn.Value)));
        }

        if (filter.ToDateVn.HasValue)
        {
            conditions.Add("[OccurredAtUtc] < @to");
            parameters.Add(DateTime2("@to", AuditTime.VietnamDateStartUtc(filter.ToDateVn.Value.AddDays(1))));
        }

        if (!string.IsNullOrWhiteSpace(filter.Username))
        {
            // CHARINDEX: tìm chuỗi con theo nghĩa đen (không có ký tự đại diện như LIKE), cùng ngữ nghĩa với Contains của EF Core.
            conditions.Add("CHARINDEX(@user, LOWER([Username])) > 0");
            parameters.Add(NVarChar("@user", filter.Username.Trim().ToLowerInvariant()));
        }

        if (!string.IsNullOrWhiteSpace(filter.Action))
        {
            conditions.Add("[Action] = @action");
            parameters.Add(NVarChar("@action", filter.Action.Trim()));
        }

        if (filter.Outcome.HasValue)
        {
            // Cột Outcome lưu tên enum dạng chữ (Success/Denied/Failed).
            conditions.Add("[Outcome] = @outcome");
            parameters.Add(NVarChar("@outcome", filter.Outcome.Value.ToString()));
        }

        var where = conditions.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", conditions);

        // 2. Chạy hai lệnh trên cùng một kết nối: đếm tổng rồi lấy đúng một trang.
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        int total;
        await using (var countCommand = new SqlCommand("SELECT COUNT(*) FROM [AuditLogs]" + where, connection))
        {
            AddCopies(countCommand, parameters);
            total = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));
        }

        var items = new List<AuditLog>();
        var pageSql =
            "SELECT " + Columns + " FROM [AuditLogs]" + where +
            " ORDER BY [AuditLogId] DESC OFFSET @offset ROWS FETCH NEXT @take ROWS ONLY";

        await using (var pageCommand = new SqlCommand(pageSql, connection))
        {
            AddCopies(pageCommand, parameters);
            pageCommand.Parameters.Add("@offset", SqlDbType.BigInt).Value = (long)pageIndex * pageSize;
            pageCommand.Parameters.Add("@take", SqlDbType.Int).Value = pageSize;

            await using SqlDataReader reader = await pageCommand.ExecuteReaderAsync(cancellationToken);

            // Lấy số thứ tự cột một lần, tránh tra tên cột cho từng dòng.
            var idOrdinal = reader.GetOrdinal("AuditLogId");
            var occurredOrdinal = reader.GetOrdinal("OccurredAtUtc");
            var userIdOrdinal = reader.GetOrdinal("UserId");
            var usernameOrdinal = reader.GetOrdinal("Username");
            var roleOrdinal = reader.GetOrdinal("RoleName");
            var actionOrdinal = reader.GetOrdinal("Action");
            var entityTypeOrdinal = reader.GetOrdinal("EntityType");
            var entityIdOrdinal = reader.GetOrdinal("EntityId");
            var outcomeOrdinal = reader.GetOrdinal("Outcome");
            var detailsOrdinal = reader.GetOrdinal("Details");
            var machineOrdinal = reader.GetOrdinal("MachineName");
            var prevHashOrdinal = reader.GetOrdinal("PrevHash");
            var hashOrdinal = reader.GetOrdinal("Hash");

            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new AuditLog
                {
                    AuditLogId = reader.GetInt64(idOrdinal),
                    // datetime2 không lưu múi giờ: ghi nhận là UTC (EF Core làm việc này bằng UtcDateTimeConverter).
                    OccurredAtUtc = DateTime.SpecifyKind(reader.GetDateTime(occurredOrdinal), DateTimeKind.Utc),
                    UserId = reader.IsDBNull(userIdOrdinal) ? null : reader.GetInt32(userIdOrdinal),
                    Username = reader.GetString(usernameOrdinal),
                    RoleName = reader.GetString(roleOrdinal),
                    Action = reader.GetString(actionOrdinal),
                    EntityType = reader.IsDBNull(entityTypeOrdinal) ? null : reader.GetString(entityTypeOrdinal),
                    EntityId = reader.IsDBNull(entityIdOrdinal) ? null : reader.GetString(entityIdOrdinal),
                    Outcome = ParseOutcome(reader.GetString(outcomeOrdinal)),
                    Details = reader.GetString(detailsOrdinal),
                    MachineName = reader.GetString(machineOrdinal),
                    PrevHash = reader.GetString(prevHashOrdinal),
                    Hash = reader.GetString(hashOrdinal)
                });
            }
        }

        return new AuditPage(items, total, pageIndex, pageSize);
    }

    /// <summary>Mỗi SqlCommand cần tham số riêng của nó (một SqlParameter không thể thuộc hai lệnh).</summary>
    private static void AddCopies(SqlCommand command, IEnumerable<SqlParameter> parameters)
    {
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(new SqlParameter(parameter.ParameterName, parameter.SqlDbType)
            {
                Size = parameter.Size,
                Value = parameter.Value
            });
        }
    }

    private static SqlParameter NVarChar(string name, string value) =>
        new(name, SqlDbType.NVarChar, 4000) { Value = value };

    private static SqlParameter DateTime2(string name, DateTime value) =>
        new(name, SqlDbType.DateTime2) { Value = value };

    /// <summary>Chỉ đúng tên enum mới hợp lệ; chữ lạ (ví dụ do bị sửa trực tiếp trong DB) cho giá trị không hợp lệ để màn xác minh chuỗi băm báo.</summary>
    private static AuditOutcome ParseOutcome(string value) => value switch
    {
        "Success" => AuditOutcome.Success,
        "Denied" => AuditOutcome.Denied,
        "Failed" => AuditOutcome.Failed,
        _ => (AuditOutcome)(-1)
    };

    /// <summary>Escapes the LIKE wildcards (and the escape character itself) so user input is matched literally.</summary>
    private static string EscapeLike(string term)
    {
        var builder = new StringBuilder(term.Length + 4);
        foreach (var ch in term)
        {
            // '[' cũng là ký tự đại diện trong LIKE của SQL Server
            if (ch is '\\' or '%' or '_' or '[')
            {
                builder.Append('\\');
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }
}

// === EF CORE EQUIVALENT ===
// Bản EF Core trước đây của cùng truy vấn này, giữ lại dạng chú thích để đối chiếu khi bảo vệ:
//
//     await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
//
//     IQueryable<AuditLog> query = db.AuditLogs.AsNoTracking();            // hoặc FromSqlInterpolated(... LIKE ...) khi có SearchText
//     if (filter.FromDateVn.HasValue) query = query.Where(a => a.OccurredAtUtc >= from);
//     if (filter.ToDateVn.HasValue)   query = query.Where(a => a.OccurredAtUtc < to);
//     if (!string.IsNullOrWhiteSpace(filter.Username))
//         query = query.Where(a => a.Username.ToLower().Contains(userTerm));   // => CHARINDEX(@p, LOWER([Username])) > 0
//     if (!string.IsNullOrWhiteSpace(filter.Action))  query = query.Where(a => a.Action == action);
//     if (filter.Outcome.HasValue)    query = query.Where(a => a.Outcome == outcome);
//
//     var total = await query.CountAsync(cancellationToken);                   // => SELECT COUNT(*) ...
//     var items = await query.OrderByDescending(a => a.AuditLogId)
//                            .Skip(pageIndex * pageSize).Take(pageSize)         // => ORDER BY ... OFFSET ... FETCH NEXT ...
//                            .ToListAsync(cancellationToken);                   // => đọc dòng, dựng AuditLog, áp value converter
//
// Khác biệt chính:
//   - EF Core: viết LINQ, EF sinh SQL + tham số + ánh xạ dòng sang đối tượng (Outcome qua ValueConverter, thời gian qua UtcDateTimeConverter).
//   - ADO.NET: tự viết SQL, tự thêm SqlParameter, tự đọc từng cột bằng SqlDataReader và tự ánh xạ; kiểm soát hoàn toàn câu lệnh
//     nhưng phải tự giữ cho đúng (tên cột, kiểu tham số, NULL, DateTimeKind, enum).
