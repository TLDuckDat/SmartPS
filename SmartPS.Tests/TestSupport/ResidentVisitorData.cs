using SmartPS.Models.Parking;

namespace SmartPS.Tests.TestSupport;

/// <summary>A zone created for a test, with its slots in SlotCode order.</summary>
public sealed record TestZone(int ZoneId, string ZoneCode, ZoneAudience Audience, IReadOnlyList<int> SlotIds, IReadOnlyList<string> SlotCodes);

/// <summary>A resident or subscriber created for a test: customer, active vehicle and (optionally) a ticket.</summary>
public sealed record TestSubscriber(int CustomerId, int CustomerVehicleId, string Plate, int VehicleTypeId, int? TicketId);

/// <summary>
/// SQL-only helpers for the resident/visitor tests. They never go through application services or a hooked
/// context factory (n7), so they work before the services exist and are not affected by command hooks.
/// Plates are always written normalized (upper-case letters and digits only).
/// </summary>
public sealed class ResidentVisitorData
{
    private static int s_counter;

    private readonly PostgresDatabaseFixture _db;

    public ResidentVisitorData(PostgresDatabaseFixture db)
    {
        _db = db;
    }

    /// <summary>Unique normalized plate, 9 characters (letters + digits), e.g. "RV3A9F1C2".</summary>
    public static string UniqueNormalizedPlate() => "RV" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();

    /// <summary>Formats a normalized plate the way an operator/OCR could type it ("rv3-a9f.1c2" style, lower case).</summary>
    public static string Decorate(string normalizedPlate)
        => (normalizedPlate[..3] + "-" + normalizedPlate[3..6] + "." + normalizedPlate[6..]).ToLowerInvariant();

    /// <summary>Unique valid Vietnamese mobile number (10 digits, 09xxxxxxxx).</summary>
    public static string UniquePhone()
    {
        var n = (Guid.NewGuid().GetHashCode() & 0x7FFFFFFF) % 100_000_000;
        return "09" + n.ToString("D8", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string NextCode(string prefix)
        => prefix + Interlocked.Increment(ref s_counter).ToString("D3", System.Globalization.CultureInfo.InvariantCulture)
                  + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();

    public Task<int> AdminUserIdAsync()
        => ScalarIntAsync("SELECT \"UserId\" FROM \"Users\" WHERE \"Username\" = 'admin'");

    public Task<int> VehicleTypeIdAsync(string typeName)
        => ScalarIntAsync("SELECT \"VehicleTypeId\" FROM \"VehicleTypes\" WHERE \"TypeName\" = @n", ("n", typeName));

    public Task<int> MotorbikeTypeIdAsync() => VehicleTypeIdAsync("Xe máy");

    public Task<int> CarTypeIdAsync() => VehicleTypeIdAsync("Xe ô tô");

    public Task<int> PlanIdAsync(string planName)
        => ScalarIntAsync("SELECT \"PlanId\" FROM \"MonthlyTicketPlans\" WHERE \"PlanName\" = @n", ("n", planName));

    public Task<int> ZoneIdAsync(string zoneCode)
        => ScalarIntAsync("SELECT \"ZoneId\" FROM \"ParkingZones\" WHERE \"ZoneCode\" = @c", ("c", zoneCode));

    public Task<int> CustomerIdByPhoneAsync(string phone)
        => ScalarIntAsync("SELECT \"CustomerId\" FROM \"Customers\" WHERE \"PhoneNumber\" = @p", ("p", phone));

    /// <summary>
    /// New vehicle type that no other test uses, so slot allocation is fully controlled by the test.
    /// With <paramref name="withPricingRule"/> it gets the motorbike rule (120 min / 5,000 / 2,000 per hour / 10,000 overnight).
    /// </summary>
    public async Task<int> CreateIsolatedVehicleTypeAsync(bool withPricingRule = true)
    {
        var id = await ScalarIntAsync(
            "INSERT INTO \"VehicleTypes\" (\"TypeName\",\"Description\") VALUES (@n,'isolated test type') RETURNING \"VehicleTypeId\"",
            ("n", NextCode("VT_")));
        if (withPricingRule)
        {
            await _db.ExecuteAsync(
                "INSERT INTO \"PricingRules\" (\"VehicleTypeId\",\"FirstBlockMinutes\",\"FirstBlockPrice\",\"AdditionalPricePerHour\",\"OvernightPrice\",\"Description\") " +
                "VALUES (@vt,120,5000,2000,10000,'test rule')",
                ("vt", id));
        }

        return id;
    }

    /// <summary>Creates a zone with <paramref name="slotCount"/> slots coded "{zoneCode}-01".. (all with <paramref name="status"/>).</summary>
    public async Task<TestZone> CreateZoneAsync(ZoneAudience audience, int vehicleTypeId, int slotCount, SlotStatus status = SlotStatus.Available, string? code = null)
    {
        var zoneCode = code ?? NextCode("Z");
        var zoneId = await ScalarIntAsync(
            "INSERT INTO \"ParkingZones\" (\"ZoneCode\",\"ZoneName\",\"TotalCapacity\",\"Description\",\"VehicleTypeId\",\"Audience\") " +
            "VALUES (@c,@n,@cap,'test zone',@vt,@a) RETURNING \"ZoneId\"",
            ("c", zoneCode), ("n", "Test " + zoneCode), ("cap", slotCount), ("vt", vehicleTypeId), ("a", (int)audience));

        var ids = new List<int>();
        var codes = new List<string>();
        for (var i = 1; i <= slotCount; i++)
        {
            var slotCode = $"{zoneCode}-{i:D2}";
            ids.Add(await ScalarIntAsync(
                "INSERT INTO \"ParkingSlots\" (\"SlotCode\",\"ZoneName\",\"ZoneId\",\"VehicleTypeId\",\"Status\") " +
                "VALUES (@sc,@zn,@z,@vt,@st) RETURNING \"SlotId\"",
                ("sc", slotCode), ("zn", "Test " + zoneCode), ("z", zoneId), ("vt", vehicleTypeId), ("st", (int)status)));
            codes.Add(slotCode);
        }

        return new TestZone(zoneId, zoneCode, audience, ids, codes);
    }

    public async Task<int> CreateCustomerAsync(bool isResident, string? apartment = null, bool isActive = true, string? fullName = null, string? phone = null)
    {
        return await ScalarIntAsync(
            "INSERT INTO \"Customers\" (\"FullName\",\"PhoneNumber\",\"DefaultLicensePlate\",\"Type\",\"CreatedAt\",\"IsActive\",\"IsResident\",\"ApartmentCode\") " +
            "VALUES (@n,@p,'',@t,now(),@a,@r,@ap) RETURNING \"CustomerId\"",
            ("n", fullName ?? NextCode("Customer ")),
            ("p", phone ?? UniquePhone()),
            ("t", isResident ? (int)CustomerType.Resident : (int)CustomerType.Regular),
            ("a", isActive),
            ("r", isResident),
            ("ap", isResident ? (apartment ?? "T-" + (Interlocked.Increment(ref s_counter) % 9999).ToString("D4", System.Globalization.CultureInfo.InvariantCulture)) : apartment));
    }

    public Task<int> AddVehicleAsync(int customerId, string normalizedPlate, int vehicleTypeId, bool isActive = true)
        => ScalarIntAsync(
            "INSERT INTO \"CustomerVehicles\" (\"CustomerId\",\"LicensePlate\",\"VehicleTypeId\",\"IsActive\",\"CreatedAt\",\"RemovedAt\") " +
            "VALUES (@c,@p,@vt,@a,now(), CASE WHEN @a THEN NULL ELSE now() END) RETURNING \"CustomerVehicleId\"",
            ("c", customerId), ("p", normalizedPlate), ("vt", vehicleTypeId), ("a", isActive));

    public Task<int> DeactivateVehicleAsync(int customerVehicleId)
        => _db.ExecuteAsync("UPDATE \"CustomerVehicles\" SET \"IsActive\" = false, \"RemovedAt\" = now() WHERE \"CustomerVehicleId\" = @id", ("id", customerVehicleId));

    /// <summary>Inserts a ticket (normalized plate) and, by default, its Kind=Create purchase covering [start, end).</summary>
    public async Task<int> CreateTicketAsync(
        int customerId,
        string normalizedPlate,
        int vehicleTypeId,
        DateTime startUtc,
        DateTime endUtc,
        MonthlyTicketStatus status = MonthlyTicketStatus.Active,
        bool withCreatePurchase = true,
        int? planId = null,
        decimal price = 120_000m)
    {
        var ticketId = await ScalarIntAsync(
            "INSERT INTO \"MonthlyTickets\" (\"TicketCode\",\"CustomerId\",\"RegisteredLicensePlate\",\"PlanId\",\"VehicleTypeId\",\"StartDate\",\"EndDate\",\"MonthlyPrice\",\"Status\",\"CreatedAt\") " +
            "VALUES (@code,@c,@p,@plan,@vt,@s,@e,@price,@st,now()) RETURNING \"TicketId\"",
            ("code", NextCode("MT-T-")), ("c", customerId), ("p", normalizedPlate), ("plan", planId), ("vt", vehicleTypeId),
            ("s", Utc(startUtc)), ("e", Utc(endUtc)), ("price", price), ("st", (int)status));
        if (withCreatePurchase)
        {
            await AddPurchaseAsync(ticketId, TicketPurchaseKind.Create, startUtc, endUtc, price, planId);
        }

        return ticketId;
    }

    public async Task<long> AddPurchaseAsync(int ticketId, TicketPurchaseKind kind, DateTime startUtc, DateTime endUtc, decimal price = 120_000m, int? planId = null)
    {
        var adminId = await AdminUserIdAsync();
        return await PostgresDatabaseFixture.ScalarAsync<long>(_db.ConnectionString,
            "INSERT INTO \"MonthlyTicketPurchases\" (\"TicketId\",\"Kind\",\"PlanId\",\"Price\",\"PeriodStartUtc\",\"PeriodEndUtc\",\"CreatedAtUtc\",\"CreatedByUserId\") " +
            "VALUES (@t,@k,@plan,@price,@s,@e,now(),@u) RETURNING \"MonthlyTicketPurchaseId\"",
            ("t", ticketId), ("k", (int)kind), ("plan", planId), ("price", price), ("s", Utc(startUtc)), ("e", Utc(endUtc)), ("u", adminId));
    }

    public Task<int> AddBlacklistAsync(string normalizedPlate, string reason = "Test blacklist reason")
        => ScalarIntAsync(
            "INSERT INTO \"BlacklistEntries\" (\"LicensePlate\",\"Reason\",\"CreatedAt\",\"IsActive\") VALUES (@p,@r,now(),true) RETURNING \"BlacklistEntryId\"",
            ("p", normalizedPlate), ("r", reason));

    public Task<int> SetSessionCheckInAsync(int sessionId, DateTime checkInUtc)
        => _db.ExecuteAsync("UPDATE \"ParkingSessions\" SET \"CheckInTime\" = @t WHERE \"SessionId\" = @id", ("t", Utc(checkInUtc)), ("id", sessionId));

    public Task<int> SetTicketEndAsync(int ticketId, DateTime endUtc)
        => _db.ExecuteAsync("UPDATE \"MonthlyTickets\" SET \"EndDate\" = @e WHERE \"TicketId\" = @id", ("e", Utc(endUtc)), ("id", ticketId));

    public Task<int> SetTicketStatusAsync(int ticketId, MonthlyTicketStatus status)
        => _db.ExecuteAsync("UPDATE \"MonthlyTickets\" SET \"Status\" = @s WHERE \"TicketId\" = @id", ("s", (int)status), ("id", ticketId));

    /// <summary>Sets PeriodEndUtc of every purchase of the ticket of the given kind.</summary>
    public Task<int> SetPurchaseEndAsync(int ticketId, TicketPurchaseKind kind, DateTime endUtc)
        => _db.ExecuteAsync("UPDATE \"MonthlyTicketPurchases\" SET \"PeriodEndUtc\" = @e WHERE \"TicketId\" = @t AND \"Kind\" = @k",
            ("e", Utc(endUtc)), ("t", ticketId), ("k", (int)kind));

    /// <summary>Resident/subscriber with an active vehicle and a ticket valid from 10 days ago to 20 days ahead (with Create purchase).</summary>
    public async Task<TestSubscriber> CreateSubscriberAsync(int vehicleTypeId, bool isResident, bool withTicket = true, bool customerActive = true, string? plate = null, string? apartment = null)
    {
        plate ??= UniqueNormalizedPlate();
        var customerId = await CreateCustomerAsync(isResident, apartment, customerActive);
        var vehicleId = await AddVehicleAsync(customerId, plate, vehicleTypeId);
        int? ticketId = null;
        if (withTicket)
        {
            var now = DateTime.UtcNow;
            ticketId = await CreateTicketAsync(customerId, plate, vehicleTypeId, now.AddDays(-10), now.AddDays(20));
        }

        return new TestSubscriber(customerId, vehicleId, plate, vehicleTypeId, ticketId);
    }

    // ---- read helpers -------------------------------------------------------------------------------------------

    public Task<long> CountAsync(string sql, params (string Name, object? Value)[] parameters)
        => PostgresDatabaseFixture.ScalarAsync<long>(_db.ConnectionString, sql, parameters);

    /// <summary>Number of ParkingSessions rows (any status) whose normalized plate equals <paramref name="normalizedPlate"/>.</summary>
    public Task<long> SessionCountForPlateAsync(string normalizedPlate, bool activeOnly = false)
        => CountAsync(
            "SELECT count(*) FROM \"ParkingSessions\" WHERE upper(regexp_replace(\"LicensePlate\", '[^a-zA-Z0-9]', '', 'g')) = @p" +
            (activeOnly ? " AND \"Status\" = 0" : string.Empty),
            ("p", normalizedPlate));

    public Task<long> ActiveSessionCountForVehicleTypeAsync(int vehicleTypeId)
        => CountAsync("SELECT count(*) FROM \"ParkingSessions\" WHERE \"VehicleTypeId\" = @vt AND \"Status\" = 0", ("vt", vehicleTypeId));

    public Task<long> OccupiedSlotCountAsync()
        => CountAsync("SELECT count(*) FROM \"ParkingSlots\" WHERE \"Status\" <> 0");

    public async Task<SlotStatus> SlotStatusAsync(int slotId)
        => (SlotStatus)await ScalarIntAsync("SELECT \"Status\" FROM \"ParkingSlots\" WHERE \"SlotId\" = @id", ("id", slotId));

    public Task<string?> SlotPlateAsync(int slotId)
        => PostgresDatabaseFixture.ScalarAsync<string>(_db.ConnectionString, "SELECT \"CurrentLicensePlate\" FROM \"ParkingSlots\" WHERE \"SlotId\" = @id", ("id", slotId));

    /// <summary>Audience of the zone that holds the slot (Mixed when the slot has no zone).</summary>
    public async Task<ZoneAudience> SlotAudienceAsync(int slotId)
        => (ZoneAudience)await ScalarIntAsync(
            "SELECT COALESCE(z.\"Audience\", 0) FROM \"ParkingSlots\" s LEFT JOIN \"ParkingZones\" z ON z.\"ZoneId\" = s.\"ZoneId\" WHERE s.\"SlotId\" = @id",
            ("id", slotId));

    public Task<string?> SlotZoneCodeAsync(int slotId)
        => PostgresDatabaseFixture.ScalarAsync<string>(_db.ConnectionString,
            "SELECT z.\"ZoneCode\" FROM \"ParkingSlots\" s LEFT JOIN \"ParkingZones\" z ON z.\"ZoneId\" = s.\"ZoneId\" WHERE s.\"SlotId\" = @id",
            ("id", slotId));

    public Task<DateTime> TicketEndAsync(int ticketId)
        => PostgresDatabaseFixture.ScalarAsync<DateTime>(_db.ConnectionString, "SELECT \"EndDate\" FROM \"MonthlyTickets\" WHERE \"TicketId\" = @id", ("id", ticketId));

    private async Task<int> ScalarIntAsync(string sql, params (string Name, object? Value)[] parameters)
        => await PostgresDatabaseFixture.ScalarAsync<int>(_db.ConnectionString, sql, parameters);

    private static DateTime Utc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
}
