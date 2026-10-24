using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using Microsoft.Data.SqlClient;
using SmartPS.Data;
using SmartPS.Models.GateControl;
using SmartPS.Models.Parking;

namespace SmartPS.Services.GateControl;

/// <summary>
/// Cấp ô đỗ trong giao dịch check-in: khoá hàng ô (UPDLOCK + READPAST: bỏ qua ô đang bị giao dịch khác khoá)
/// rồi chiếm ô bằng cập nhật có điều kiện.
/// Chỉ mục duy nhất IX_ParkingSessions_SlotId_Active là chốt chặn cuối cùng ở tầng cơ sở dữ liệu.
/// </summary>
public static class GateSlotAllocator
{
    public const string ActiveSlotIndexName = "IX_ParkingSessions_SlotId_Active";
    public const int MaxAutoAttempts = 3;

    // 2601: trùng khoá ở chỉ mục duy nhất; 2627: trùng khoá ở ràng buộc UNIQUE/PRIMARY KEY
    private static readonly int[] UniqueViolationNumbers = { 2601, 2627 };

    private const string SelectColumns = """
        SELECT TOP (1) s."SlotId", s."SlotCode", s."VehicleTypeId", s."Status", COALESCE(z."Audience", 0) AS "Audience", s."ZoneId", z."ZoneCode",
               CAST(CASE WHEN EXISTS (SELECT 1 FROM "ParkingSessions" p WHERE p."SlotId" = s."SlotId" AND p."Status" = 0)
                         THEN 1 ELSE 0 END AS bit) AS "HasActiveSession"
        """;

    public static async Task<SlotCandidate?> LockNextAsync(
        SmartPsDbContext db,
        int vehicleTypeId,
        VehicleCategory category,
        IReadOnlyCollection<int> excludedSlotIds,
        CancellationToken cancellationToken = default)
    {
        var transaction = RequireTransaction(db);

        var allowed = SlotAllocationPolicy.GetAudiencePreference(category).Select(a => (int)a).ToArray();
        if (allowed.Length == 0)
        {
            return null;
        }

        await using var command = CreateCommand(db, transaction, string.Empty);
        command.Parameters.Add(new SqlParameter("vt", SqlDbType.Int) { Value = vehicleTypeId });

        // Thứ tự ưu tiên nhóm khu: vị trí trong danh sách allowed (thay array_position của PostgreSQL)
        var allowedNames = AddIntParameters(command, "a", allowed);
        var preference = string.Join(" ", allowedNames.Select((name, index) => $"WHEN {name} THEN {index}"));
        var excludedFilter = excludedSlotIds.Count == 0
            ? string.Empty
            : $"""AND s."SlotId" NOT IN ({string.Join(", ", AddIntParameters(command, "x", excludedSlotIds))})""";

        command.CommandText = SelectColumns + $"""

            FROM "ParkingSlots" s WITH (UPDLOCK, ROWLOCK, READPAST) LEFT JOIN "ParkingZones" z ON z."ZoneId" = s."ZoneId"
            WHERE s."VehicleTypeId" = @vt AND s."Status" = 0
              AND COALESCE(z."Audience", 0) IN ({string.Join(", ", allowedNames)}) {excludedFilter}
              AND NOT EXISTS (SELECT 1 FROM "ParkingSessions" p WHERE p."SlotId" = s."SlotId" AND p."Status" = 0)
            ORDER BY CASE COALESCE(z."Audience", 0) {preference} END, s."SlotCode" COLLATE Latin1_General_BIN2, s."SlotId"
            """;
        return await ReadSingleAsync(command, cancellationToken);
    }

    public static async Task<SlotCandidate?> LockByIdAsync(SmartPsDbContext db, int slotId, CancellationToken cancellationToken = default)
    {
        var transaction = RequireTransaction(db);

        // Ô được yêu cầu cụ thể: chờ khoá (không READPAST) để kiểm tra đúng trạng thái sau khi giao dịch kia kết thúc
        var sql = SelectColumns + """

            FROM "ParkingSlots" s WITH (UPDLOCK, ROWLOCK) LEFT JOIN "ParkingZones" z ON z."ZoneId" = s."ZoneId"
            WHERE s."SlotId" = @id
            """;

        await using var command = CreateCommand(db, transaction, sql);
        command.Parameters.Add(new SqlParameter("id", SqlDbType.Int) { Value = slotId });
        return await ReadSingleAsync(command, cancellationToken);
    }

    public static async Task<bool> TryOccupyAsync(SmartPsDbContext db, int slotId, string licensePlate, CancellationToken cancellationToken = default)
    {
        var transaction = RequireTransaction(db);

        await using var command = CreateCommand(db, transaction,
            """UPDATE "ParkingSlots" SET "Status" = 1, "CurrentLicensePlate" = @plate WHERE "SlotId" = @id AND "Status" = 0""");
        command.Parameters.Add(new SqlParameter("plate", SqlDbType.NVarChar, 20) { Value = licensePlate });
        command.Parameters.Add(new SqlParameter("id", SqlDbType.Int) { Value = slotId });
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static List<string> AddIntParameters(DbCommand command, string prefix, IEnumerable<int> values)
    {
        var names = new List<string>();
        foreach (var value in values)
        {
            var name = $"@{prefix}{names.Count}";
            command.Parameters.Add(new SqlParameter(name, SqlDbType.Int) { Value = value });
            names.Add(name);
        }

        return names;
    }

    public static Task<bool> VehicleTypeHasSlotsAsync(SmartPsDbContext db, int vehicleTypeId, CancellationToken cancellationToken = default)
        => db.ParkingSlots.AsNoTracking().AnyAsync(s => s.VehicleTypeId == vehicleTypeId, cancellationToken);

    public static async Task<(SlotCandidate? Slot, CheckInRejectReason Reason)> AllocateAsync(
        SmartPsDbContext db,
        int vehicleTypeId,
        VehicleCategory category,
        int? requestedSlotId,
        string licensePlate,
        CancellationToken cancellationToken = default)
    {
        RequireTransaction(db);

        if (requestedSlotId.HasValue)
        {
            var requested = await LockByIdAsync(db, requestedSlotId.Value, cancellationToken);
            var reason = SlotAllocationPolicy.ValidateRequestedSlot(requested, vehicleTypeId, category);
            if (reason != CheckInRejectReason.None)
            {
                return (null, reason);
            }

            if (!await TryOccupyAsync(db, requested!.SlotId, licensePlate, cancellationToken))
            {
                return (null, CheckInRejectReason.SlotNotAvailable);
            }

            return (requested with { Status = SlotStatus.Occupied }, CheckInRejectReason.None);
        }

        var excluded = new List<int>();
        for (var attempt = 0; attempt < MaxAutoAttempts; attempt++)
        {
            var candidate = await LockNextAsync(db, vehicleTypeId, category, excluded, cancellationToken);
            if (candidate is null)
            {
                break;
            }

            if (await TryOccupyAsync(db, candidate.SlotId, licensePlate, cancellationToken))
            {
                return (candidate with { Status = SlotStatus.Occupied }, CheckInRejectReason.None);
            }

            excluded.Add(candidate.SlotId);
        }

        // Loại xe chưa cấu hình ô nào: giữ hành vi cũ (không gán ô).
        if (!await VehicleTypeHasSlotsAsync(db, vehicleTypeId, cancellationToken))
        {
            return (null, CheckInRejectReason.None);
        }

        return (null, CheckInRejectReason.NoSlotAvailable);
    }

    /// <summary>True when the exception is a duplicate active session on one slot (2601/2627 on <see cref="ActiveSlotIndexName"/>).</summary>
    public static bool IsActiveSlotUniqueViolation(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            // SQL Server chỉ ghi tên chỉ mục trong nội dung thông báo lỗi
            if (current is SqlException sql
                && sql.Errors.Cast<SqlError>().Any(e => UniqueViolationNumbers.Contains(e.Number)
                    && e.Message.Contains(ActiveSlotIndexName, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    private static IDbContextTransaction RequireTransaction(SmartPsDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Database.CurrentTransaction
               ?? throw new InvalidOperationException("Slot allocation must run inside the check-in transaction.");
    }

    private static DbCommand CreateCommand(SmartPsDbContext db, IDbContextTransaction transaction, string sql)
    {
        var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction.GetDbTransaction();
        return command;
    }

    private static async Task<SlotCandidate?> ReadSingleAsync(DbCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new SlotCandidate(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetInt32(2),
            (SlotStatus)reader.GetInt32(3),
            (ZoneAudience)reader.GetInt32(4),
            reader.IsDBNull(5) ? null : reader.GetInt32(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetBoolean(7));
    }
}
