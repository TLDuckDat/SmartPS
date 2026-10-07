using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartPS.Models.Parking;
using SmartPS.Models.Shifts;

namespace SmartPS.Tests.TestSupport;

/// <summary>
/// Direct inserts (typed enums, explicit UTC instants) for reporting tests. Nothing goes through application services,
/// so the data is exactly what a test describes. Every instant passed in must be UTC.
/// </summary>
public sealed class ReportSeed
{
    /// <summary>Opening time of the shared seed shift; deliberately outside every test window (2031-xx).</summary>
    public static readonly DateTime SeedShiftOpenedAtUtc = new(2030, 6, 1, 1, 0, 0, DateTimeKind.Utc);

    private static int s_counter;

    private readonly PostgresDatabaseFixture _db;
    private int? _adminId;
    private int? _shiftId;
    private int? _motorbikeId;

    public ReportSeed(PostgresDatabaseFixture db)
    {
        _db = db;
    }

    public static string UniquePlate(string prefix = "RP")
        => prefix + Interlocked.Increment(ref s_counter).ToString("D4", System.Globalization.CultureInfo.InvariantCulture)
                  + Guid.NewGuid().ToString("N")[..3].ToUpperInvariant();

    public async Task<int> AdminUserIdAsync()
    {
        if (_adminId is null)
        {
            await using var db = await _db.Factory.CreateDbContextAsync();
            _adminId = await db.Users.Where(u => u.Username == TestUsers.SeedAdminUsername).Select(u => u.UserId).SingleAsync();
        }

        return _adminId.Value;
    }

    public async Task<string> AdminFullNameAsync()
    {
        await using var db = await _db.Factory.CreateDbContextAsync();
        return await db.Users.Where(u => u.Username == TestUsers.SeedAdminUsername).Select(u => u.FullName).SingleAsync();
    }

    public async Task<int> VehicleTypeIdAsync(string typeName)
    {
        await using var db = await _db.Factory.CreateDbContextAsync();
        return await db.VehicleTypes.Where(v => v.TypeName == typeName).Select(v => v.VehicleTypeId).SingleAsync();
    }

    public async Task<int> MotorbikeTypeIdAsync()
    {
        _motorbikeId ??= await VehicleTypeIdAsync("Xe máy");
        return _motorbikeId.Value;
    }

    public Task<int> CarTypeIdAsync() => VehicleTypeIdAsync("Xe ô tô");

    /// <summary>The shared Locked shift every seeded FinancialTransaction belongs to (created on first use).</summary>
    public async Task<int> ShiftIdAsync()
    {
        _shiftId ??= (await CreateShiftAsync(SeedShiftOpenedAtUtc, SeedShiftOpenedAtUtc.AddHours(8))).ShiftId;
        return _shiftId.Value;
    }

    public async Task<Shift> CreateShiftAsync(DateTime openedAtUtc, DateTime? closedAtUtc = null, ShiftStatus status = ShiftStatus.Locked,
        decimal beginningCash = 100_000m, decimal? expectedCash = 150_000m, decimal? actualCash = 149_000m)
    {
        var shift = new Shift
        {
            OpenedByUserId = await AdminUserIdAsync(),
            OpenedAt = Utc(openedAtUtc),
            BeginningCash = beginningCash,
            ClosedAt = closedAtUtc is null ? null : Utc(closedAtUtc.Value),
            ExpectedCash = closedAtUtc is null ? null : expectedCash,
            ActualCash = closedAtUtc is null ? null : actualCash,
            Difference = closedAtUtc is null || expectedCash is null || actualCash is null ? null : actualCash - expectedCash,
            Status = status
        };
        await using var db = await _db.Factory.CreateDbContextAsync();
        db.Shifts.Add(shift);
        await db.SaveChangesAsync();
        return shift;
    }

    /// <summary>
    /// Inserts one session. Status defaults to Completed when <paramref name="checkOutUtc"/> is set, otherwise Active.
    /// Vehicle type defaults to the seed motorbike type.
    /// </summary>
    public async Task<ParkingSession> SessionAsync(
        DateTime checkInUtc,
        DateTime? checkOutUtc = null,
        CustomerType customerType = CustomerType.Regular,
        bool isMonthlyPass = false,
        int? vehicleTypeId = null,
        int? slotId = null,
        string? plate = null,
        decimal totalFee = 0m,
        PaymentMethod paymentMethod = PaymentMethod.Cash,
        SessionStatus? status = null)
    {
        var session = await BuildSessionAsync(checkInUtc, checkOutUtc, customerType, isMonthlyPass, vehicleTypeId, slotId, plate, totalFee, paymentMethod, status);
        await using var db = await _db.Factory.CreateDbContextAsync();
        db.ParkingSessions.Add(session);
        await db.SaveChangesAsync();
        return session;
    }

    /// <summary>Inserts <paramref name="count"/> completed-or-active sessions in one SaveChanges; <paramref name="checkIn"/> gives the i-th check-in.</summary>
    public async Task<IReadOnlyList<ParkingSession>> SessionsAsync(int count, Func<int, DateTime> checkIn, Func<int, DateTime?>? checkOut = null,
        CustomerType customerType = CustomerType.Regular, bool isMonthlyPass = false, int? vehicleTypeId = null)
    {
        var sessions = new List<ParkingSession>(count);
        for (var i = 0; i < count; i++)
        {
            sessions.Add(await BuildSessionAsync(checkIn(i), checkOut?.Invoke(i), customerType, isMonthlyPass, vehicleTypeId, null, null, 0m, PaymentMethod.Cash, null));
        }

        await using var db = await _db.Factory.CreateDbContextAsync();
        db.ParkingSessions.AddRange(sessions);
        await db.SaveChangesAsync();
        return sessions;
    }

    /// <summary>Inserts one FinancialTransaction (Refund amounts are passed already negative, like ShiftService/PaymentService store them).</summary>
    public async Task<FinancialTransaction> FinancialAsync(FinancialTransactionType type, PaymentMethod method, decimal amount, DateTime createdAtUtc, int? sessionId = null)
    {
        var tx = new FinancialTransaction
        {
            TransactionCode = "RPT-FT-" + Guid.NewGuid().ToString("N")[..20].ToUpperInvariant(),
            ShiftId = await ShiftIdAsync(),
            ParkingSessionId = sessionId,
            CreatedByUserId = await AdminUserIdAsync(),
            Type = type,
            PaymentMethod = method,
            Amount = amount,
            CreatedAt = Utc(createdAtUtc),
            Note = "report test"
        };
        await using var db = await _db.Factory.CreateDbContextAsync();
        db.FinancialTransactions.Add(tx);
        await db.SaveChangesAsync();
        return tx;
    }

    /// <summary>Completed session plus its single ParkingFee transaction recorded at check-out time.</summary>
    public async Task<ParkingSession> PaidSessionAsync(DateTime checkInUtc, DateTime checkOutUtc, decimal fee, PaymentMethod method = PaymentMethod.Cash,
        CustomerType customerType = CustomerType.Regular, bool isMonthlyPass = false, int? vehicleTypeId = null, string? plate = null, int? slotId = null)
    {
        var session = await SessionAsync(checkInUtc, checkOutUtc, customerType, isMonthlyPass, vehicleTypeId, slotId, plate, fee, method);
        await FinancialAsync(FinancialTransactionType.ParkingFee, method, fee, checkOutUtc, session.SessionId);
        return session;
    }

    /// <summary>
    /// M2: a customer (<paramref name="isResident"/>), one monthly ticket and its MonthlyTicketPurchases rows with explicit
    /// <c>Kind</c>, <c>Price</c> and <c>CreatedAtUtc</c> (Task 1 ADDENDUM A table).
    /// </summary>
    public async Task<(int CustomerId, int TicketId)> CreateTicketWithPurchasesAsync(
        bool isResident,
        int vehicleTypeId,
        params (TicketPurchaseKind Kind, decimal Price, DateTime CreatedAtUtc)[] purchases)
    {
        if (purchases.Length == 0)
        {
            throw new ArgumentException("At least one purchase is required.", nameof(purchases));
        }

        var data = new ResidentVisitorData(_db);
        var customerId = await data.CreateCustomerAsync(isResident);
        var plate = ResidentVisitorData.UniqueNormalizedPlate();
        await data.AddVehicleAsync(customerId, plate, vehicleTypeId);

        var ordered = purchases.OrderBy(p => p.CreatedAtUtc).ToArray();
        var start = Utc(ordered[0].CreatedAtUtc);
        var end = Utc(ordered[^1].CreatedAtUtc).AddDays(30);
        var ticketId = await data.CreateTicketAsync(customerId, plate, vehicleTypeId, start, end,
            withCreatePurchase: false, price: ordered[^1].Price);

        var adminId = await AdminUserIdAsync();
        var periodStart = start;
        foreach (var p in ordered)
        {
            await _db.ExecuteAsync(
                "INSERT INTO \"MonthlyTicketPurchases\" (\"TicketId\",\"Kind\",\"PlanId\",\"Price\",\"PeriodStartUtc\",\"PeriodEndUtc\",\"CreatedAtUtc\",\"CreatedByUserId\") " +
                "VALUES (@t,@k,NULL,@price,@s,@e,@c,@u)",
                ("t", ticketId), ("k", (int)p.Kind), ("price", p.Price), ("s", periodStart), ("e", periodStart.AddDays(30)), ("c", Utc(p.CreatedAtUtc)), ("u", adminId));
            periodStart = periodStart.AddDays(30);
        }

        return (customerId, ticketId);
    }

    /// <summary>Totals of a performance seed, computed from the inserted rows.</summary>
    public sealed record PerformanceSeed(ReportDateRange Range, int Sessions, int CompletedSessions, int FinancialTransactions, int ShiftId);

    /// <summary>
    /// AC-10 / N1 data set: <paramref name="sessions"/> sessions with check-ins spread pseudo-randomly (setseed 0.42) over
    /// <paramref name="days"/> VN days from <paramref name="startVn"/>, 95% Completed; one ParkingFee per completed session
    /// (respects IX_FinancialTransactions_OneParkingFeePerSession); the remaining transactions up to
    /// <paramref name="financials"/> are session-less Refunds / Adjustments. Mixed CustomerType / IsMonthlyPass, motorbike and car.
    /// One Locked shift opened by the seed admin owns every transaction (ADDENDUM N3). Codes are 'PERF-' || g.
    /// </summary>
    public static async Task<PerformanceSeed> SeedPerformanceAsync(PostgresDatabaseFixture fixture, DateOnly startVn, int days = 30,
        int sessions = 50_000, int financials = 50_000)
    {
        var seed = new ReportSeed(fixture);
        var range = new ReportDateRange(startVn, startVn.AddDays(days - 1));
        var fromUtc = AuditTime.VietnamDateStartUtc(range.FromVn);
        var toUtc = AuditTime.VietnamDateStartUtc(range.ToVn.AddDays(1));
        var spanSeconds = (toUtc - fromUtc).TotalSeconds - 1;
        var adminId = await seed.AdminUserIdAsync();
        var moto = await seed.MotorbikeTypeIdAsync();
        var car = await seed.CarTypeIdAsync();
        var shift = await seed.CreateShiftAsync(fromUtc.AddMinutes(-30), toUtc.AddHours(1));

        const string sql = """
            SELECT setseed(0.42);

            INSERT INTO "ParkingSessions"
                ("TicketCode","LicensePlate","VehicleTypeId","SlotId","CheckInTime","CheckOutTime","TotalFee","Status","PaymentMethod",
                 "CustomerId","IsMonthlyPass","CustomerType","CreatedByUserId")
            SELECT 'PERF-' || x.g,
                   'PF' || lpad((x.g % 7919)::text, 6, '0'),
                   CASE WHEN x.g % 3 = 0 THEN @car ELSE @moto END,
                   NULL,
                   x.ci,
                   CASE WHEN x.completed THEN LEAST(x.ci + x.dur, @to - interval '1 second') END,
                   CASE WHEN NOT x.completed OR x.g % 10 = 9 OR x.g % 7 IN (0, 1) THEN 0 ELSE 5000 END,
                   CASE WHEN x.completed THEN 1 ELSE 0 END,
                   CASE WHEN x.g % 10 = 9 OR x.g % 7 IN (0, 1) THEN 3 WHEN x.g % 10 IN (6, 7) THEN 1 WHEN x.g % 10 = 8 THEN 2 ELSE 0 END,
                   NULL,
                   x.g % 7 IN (0, 1),
                   CASE WHEN x.g % 7 = 0 THEN 3 WHEN x.g % 11 = 0 THEN 2 ELSE 0 END,
                   @admin
            FROM (
                SELECT g,
                       @from + (random() * @span) * interval '1 second' AS ci,
                       (300 + random() * 14400) * interval '1 second' AS dur,
                       g % 20 <> 0 AS completed
                FROM generate_series(1, @sessions) g
            ) x;

            WITH done AS (
                SELECT s."SessionId", s."PaymentMethod", s."TotalFee", s."CheckOutTime",
                       row_number() OVER (ORDER BY s."SessionId") AS rn
                FROM "ParkingSessions" s
                WHERE s."TicketCode" LIKE 'PERF-%' AND s."Status" = 1
            )
            INSERT INTO "FinancialTransactions"
                ("TransactionCode","ShiftId","ParkingSessionId","CreatedByUserId","Type","PaymentMethod","Amount","CreatedAt","Note")
            SELECT 'PERF-' || g,
                   @shift,
                   d."SessionId",
                   @admin,
                   CASE WHEN d."SessionId" IS NOT NULL THEN 0 WHEN g % 2 = 0 THEN 1 ELSE 2 END,
                   CASE WHEN d."SessionId" IS NOT NULL THEN d."PaymentMethod" ELSE 0 END,
                   CASE WHEN d."SessionId" IS NOT NULL THEN d."TotalFee" WHEN g % 2 = 0 THEN -5000 ELSE 2000 END,
                   COALESCE(d."CheckOutTime", @from + (random() * @span) * interval '1 second'),
                   'perf seed'
            FROM generate_series(1, @financials) g
            LEFT JOIN done d ON d.rn = g;

            ANALYZE "ParkingSessions";
            ANALYZE "FinancialTransactions";
            """;

        await using (var conn = new NpgsqlConnection(fixture.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(sql, conn) { CommandTimeout = 600 };
            cmd.Parameters.AddWithValue("from", fromUtc);
            cmd.Parameters.AddWithValue("to", toUtc);
            cmd.Parameters.AddWithValue("span", spanSeconds);
            cmd.Parameters.AddWithValue("sessions", sessions);
            cmd.Parameters.AddWithValue("financials", financials);
            cmd.Parameters.AddWithValue("moto", moto);
            cmd.Parameters.AddWithValue("car", car);
            cmd.Parameters.AddWithValue("admin", adminId);
            cmd.Parameters.AddWithValue("shift", shift.ShiftId);
            await cmd.ExecuteNonQueryAsync();
        }

        var total = await fixture.ScalarAsync<long>("SELECT count(*) FROM \"ParkingSessions\" WHERE \"TicketCode\" LIKE 'PERF-%'");
        var completed = await fixture.ScalarAsync<long>("SELECT count(*) FROM \"ParkingSessions\" WHERE \"TicketCode\" LIKE 'PERF-%' AND \"Status\" = 1");
        var fts = await fixture.ScalarAsync<long>("SELECT count(*) FROM \"FinancialTransactions\" WHERE \"TransactionCode\" LIKE 'PERF-%'");
        return new PerformanceSeed(range, (int)total, (int)completed, (int)fts, shift.ShiftId);
    }

    private async Task<ParkingSession> BuildSessionAsync(DateTime checkInUtc, DateTime? checkOutUtc, CustomerType customerType, bool isMonthlyPass,
        int? vehicleTypeId, int? slotId, string? plate, decimal totalFee, PaymentMethod paymentMethod, SessionStatus? status)
    {
        return new ParkingSession
        {
            TicketCode = "RPT-" + Guid.NewGuid().ToString("N")[..16].ToUpperInvariant(),
            LicensePlate = plate ?? UniquePlate(),
            VehicleTypeId = vehicleTypeId ?? await MotorbikeTypeIdAsync(),
            SlotId = slotId,
            CheckInTime = Utc(checkInUtc),
            CheckOutTime = checkOutUtc is null ? null : Utc(checkOutUtc.Value),
            TotalFee = totalFee,
            Status = status ?? (checkOutUtc is null ? SessionStatus.Active : SessionStatus.Completed),
            PaymentMethod = paymentMethod,
            IsMonthlyPass = isMonthlyPass,
            CustomerType = customerType,
            CreatedByUserId = await AdminUserIdAsync()
        };
    }

    private static DateTime Utc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
}
