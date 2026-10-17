using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using SmartPS.Data;
using SmartPS.Models.GateControl;
using SmartPS.Models.Parking;

namespace SmartPS.Services.GateControl;

/// <summary>
/// Cấp ô đỗ trong giao dịch check-in: khoá hàng ô (FOR UPDATE SKIP LOCKED) rồi chiếm ô bằng cập nhật có điều kiện.
/// Chỉ mục duy nhất IX_ParkingSessions_SlotId_Active là chốt chặn cuối cùng ở tầng cơ sở dữ liệu.
/// </summary>
public static class GateSlotAllocator
{
    public const string ActiveSlotIndexName = "IX_ParkingSessions_SlotId_Active";
    public const int MaxAutoAttempts = 3;

    private const string SelectColumns = """
        SELECT s."SlotId", s."SlotCode", s."VehicleTypeId", s."Status", COALESCE(z."Audience", 0) AS "Audience", s."ZoneId", z."ZoneCode",
               EXISTS (SELECT 1 FROM "ParkingSessions" p WHERE p."SlotId" = s."SlotId" AND p."Status" = 0) AS "HasActiveSession"
        FROM "ParkingSlots" s LEFT JOIN "ParkingZones" z ON z."ZoneId" = s."ZoneId"
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

        var sql = SelectColumns + """

            WHERE s."VehicleTypeId" = @vt AND s."Status" = 0
              AND COALESCE(z."Audience", 0) = ANY(@allowed) AND NOT (s."SlotId" = ANY(@excluded))
              AND NOT EXISTS (SELECT 1 FROM "ParkingSessions" p WHERE p."SlotId" = s."SlotId" AND p."Status" = 0)
            ORDER BY array_position(@allowed, COALESCE(z."Audience", 0)), s."SlotCode" COLLATE "C", s."SlotId"
            LIMIT 1
            FOR UPDATE OF s SKIP LOCKED
            """;

        await using var command = CreateCommand(db, transaction, sql);
        command.Parameters.Add(new NpgsqlParameter("vt", vehicleTypeId));
        command.Parameters.Add(new NpgsqlParameter("allowed", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = allowed });
        command.Parameters.Add(new NpgsqlParameter("excluded", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = excludedSlotIds.ToArray() });
        return await ReadSingleAsync(command, cancellationToken);
    }

    public static async Task<SlotCandidate?> LockByIdAsync(SmartPsDbContext db, int slotId, CancellationToken cancellationToken = default)
    {
        var transaction = RequireTransaction(db);

        var sql = SelectColumns + """

            WHERE s."SlotId" = @id
            FOR UPDATE OF s
            """;

        await using var command = CreateCommand(db, transaction, sql);
        command.Parameters.Add(new NpgsqlParameter("id", slotId));
        return await ReadSingleAsync(command, cancellationToken);
    }

    public static async Task<bool> TryOccupyAsync(SmartPsDbContext db, int slotId, string licensePlate, CancellationToken cancellationToken = default)
    {
        var transaction = RequireTransaction(db);

        await using var command = CreateCommand(db, transaction,
            """UPDATE "ParkingSlots" SET "Status" = 1, "CurrentLicensePlate" = @plate WHERE "SlotId" = @id AND "Status" = 0""");
        command.Parameters.Add(new NpgsqlParameter("plate", licensePlate));
        command.Parameters.Add(new NpgsqlParameter("id", slotId));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
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

    /// <summary>True when the exception is a duplicate active session on one slot (23505 on <see cref="ActiveSlotIndexName"/>).</summary>
    public static bool IsActiveSlotUniqueViolation(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
                && string.Equals(pg.ConstraintName, ActiveSlotIndexName, StringComparison.Ordinal))
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
