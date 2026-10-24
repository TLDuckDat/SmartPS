using System.Globalization;
using SmartPS.Models.Parking;

namespace SmartPS.Tests.TestSupport;

/// <summary>A zone created for a test, with its slots in SlotCode order.</summary>
public sealed record TestZone(int ZoneId, string ZoneCode, ZoneAudience Audience, IReadOnlyList<int> SlotIds, IReadOnlyList<string> SlotCodes);

/// <summary>
/// A vehicle owner created for a test on the household/vehicle model: customer (optionally in a household),
/// one vehicle stored with a RAW plate, and optionally a monthly ticket.
/// </summary>
public sealed record TestOwner(
    int CustomerId,
    int? HouseholdId,
    string? ApartmentCode,
    int VehicleId,
    int VehicleTypeId,
    string RawPlate,
    string NormalizedPlate,
    int? TicketId,
    string? TicketCode);

/// <summary>
/// SQL-only seeding and read helpers for the gate tests. They write the household/vehicle tables directly
/// (Households, Customers, Vehicles, MonthlyTickets, BlacklistedVehicles), never through application services and
/// never through a hooked context factory, so command hooks of a test do not see them.
/// Plates are stored RAW (e.g. "RV3-A9F.1C2"), the way the customer screens store them; the gate must normalize both sides.
/// </summary>
public sealed class GateTestData
{
    private static int s_counter;

    private readonly SqlServerDatabaseFixture _db;

    public GateTestData(SqlServerDatabaseFixture db)
    {
        _db = db;
    }

    // ---- plates ---------------------------------------------------------------------------------------------------

    /// <summary>Unique normalized plate, 9 characters (upper-case letters + digits), e.g. "RV3A9F1C2".</summary>
    public static string UniqueNormalizedPlate() => "RV" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();

    /// <summary>Raw stored form of a normalized plate, like the customer screens keep it: "RV3-A9F.1C2".</summary>
    public static string Raw(string normalizedPlate)
        => normalizedPlate[..3] + "-" + normalizedPlate[3..6] + "." + normalizedPlate[6..];

    /// <summary>How an operator/OCR could type the plate at the gate: lower case with other separators ("rv3 a9f-1c2").</summary>
    public static string Decorate(string normalizedPlate)
        => (normalizedPlate[..3] + " " + normalizedPlate[3..6] + "-" + normalizedPlate[6..]).ToLowerInvariant();

    /// <summary>Unique valid Vietnamese mobile number (10 digits, 09xxxxxxxx).</summary>
    public static string UniquePhone()
    {
        var n = (Guid.NewGuid().GetHashCode() & 0x7FFFFFFF) % 100_000_000;
        return "09" + n.ToString("D8", CultureInfo.InvariantCulture);
    }

    private static string NextCode(string prefix)
        => prefix + Interlocked.Increment(ref s_counter).ToString("D3", CultureInfo.InvariantCulture)
                  + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();

    // ---- lookups --------------------------------------------------------------------------------------------------

    public Task<int> AdminUserIdAsync()
        => ScalarIntAsync("SELECT \"UserId\" FROM \"Users\" WHERE \"Username\" = 'admin'");

    public Task<int> VehicleTypeIdAsync(string typeName)
        => ScalarIntAsync("SELECT \"VehicleTypeId\" FROM \"VehicleTypes\" WHERE \"TypeName\" = @n", ("n", typeName));

    public Task<int> MotorbikeTypeIdAsync() => VehicleTypeIdAsync("Xe máy");

    public Task<int> CarTypeIdAsync() => VehicleTypeIdAsync("Xe ô tô");

    public Task<int> ZoneIdAsync(string zoneCode)
        => ScalarIntAsync("SELECT \"ZoneId\" FROM \"ParkingZones\" WHERE \"ZoneCode\" = @c", ("c", zoneCode));

    // ---- vehicle types, pricing, zones ----------------------------------------------------------------------------

    /// <summary>
    /// New vehicle type that no other test uses, so slot allocation is fully controlled by the test.
    /// With <paramref name="withPricingRule"/> it gets a PricingRule (4h block / daily / monthly prices).
    /// </summary>
    public async Task<int> CreateIsolatedVehicleTypeAsync(bool withPricingRule = true, decimal block4hPrice = 5_000m, decimal dailyPrice = 25_000m)
    {
        var id = await ScalarIntAsync(
            "INSERT INTO \"VehicleTypes\" (\"TypeName\",\"Description\") OUTPUT inserted.\"VehicleTypeId\" VALUES (@n,'isolated test type')",
            ("n", NextCode("VT_")));
        if (withPricingRule)
        {
            await _db.ExecuteAsync(
                "INSERT INTO \"PricingRules\" (\"VehicleTypeId\",\"Block4hPrice\",\"DailyPrice\",\"Monthly1Price\",\"Monthly3Price\",\"Monthly6Price\",\"Description\") " +
                "VALUES (@vt,@b,@d,100000,285000,540000,'test rule')",
                ("vt", id), ("b", block4hPrice), ("d", dailyPrice));
        }

        return id;
    }

    /// <summary>Creates a zone with <paramref name="slotCount"/> slots coded "{zoneCode}-01".. (all with <paramref name="status"/>).</summary>
    public async Task<TestZone> CreateZoneAsync(ZoneAudience audience, int vehicleTypeId, int slotCount, SlotStatus status = SlotStatus.Available, string? code = null)
    {
        var zoneCode = code ?? NextCode("Z");
        var zoneId = await ScalarIntAsync(
            "INSERT INTO \"ParkingZones\" (\"ZoneCode\",\"ZoneName\",\"TotalCapacity\",\"Description\",\"VehicleTypeId\",\"Audience\") " +
            "OUTPUT inserted.\"ZoneId\" VALUES (@c,@n,@cap,'test zone',@vt,@a)",
            ("c", zoneCode), ("n", "Test " + zoneCode), ("cap", slotCount), ("vt", vehicleTypeId), ("a", (int)audience));

        var ids = new List<int>();
        var codes = new List<string>();
        for (var i = 1; i <= slotCount; i++)
        {
            var slotCode = $"{zoneCode}-{i:D2}";
            ids.Add(await ScalarIntAsync(
                "INSERT INTO \"ParkingSlots\" (\"SlotCode\",\"ZoneName\",\"ZoneId\",\"VehicleTypeId\",\"Status\") " +
                "OUTPUT inserted.\"SlotId\" VALUES (@sc,@zn,@z,@vt,@st)",
                ("sc", slotCode), ("zn", "Test " + zoneCode), ("z", zoneId), ("vt", vehicleTypeId), ("st", (int)status)));
            codes.Add(slotCode);
        }

        return new TestZone(zoneId, zoneCode, audience, ids, codes);
    }

    // ---- households, customers, vehicles, tickets, blacklist --------------------------------------------------------

    /// <summary>Unique apartment code (fits Households.ApartmentCode, max 20).</summary>
    public static string UniqueApartment() => NextCode("T-");

    public Task<int> CreateHouseholdAsync(string? apartment = null, bool isActive = true, int? maxVehicles = null)
        => ScalarIntAsync(
            "INSERT INTO \"Households\" (\"ApartmentCode\",\"MaxVehicles\",\"IsActive\",\"CreatedAt\") OUTPUT inserted.\"HouseholdId\" VALUES (@a,@m,@act,SYSUTCDATETIME())",
            ("a", apartment ?? UniqueApartment()), ("m", maxVehicles), ("act", isActive));

    public Task<int> SetHouseholdActiveAsync(int householdId, bool isActive)
        => _db.ExecuteAsync("UPDATE \"Households\" SET \"IsActive\" = @a WHERE \"HouseholdId\" = @id", ("a", isActive), ("id", householdId));

    /// <summary>
    /// Inserts a customer. <paramref name="type"/> is written as given and may deliberately disagree with
    /// <paramref name="householdId"/>: residency (D1) must come from the household, never from <c>Customer.Type</c>.
    /// </summary>
    public Task<int> CreateCustomerAsync(int? householdId, CustomerType type = CustomerType.External, bool isActive = true, string? fullName = null)
        => ScalarIntAsync(
            "INSERT INTO \"Customers\" (\"FullName\",\"PhoneNumber\",\"Type\",\"HouseholdId\",\"CreatedAt\",\"IsActive\") " +
            "OUTPUT inserted.\"CustomerId\" VALUES (@n,@p,@t,@h,SYSUTCDATETIME(),@a)",
            ("n", fullName ?? NextCode("Customer ")), ("p", UniquePhone()), ("t", (int)type), ("h", householdId), ("a", isActive));

    public Task<int> SetCustomerActiveAsync(int customerId, bool isActive)
        => _db.ExecuteAsync("UPDATE \"Customers\" SET \"IsActive\" = @a WHERE \"CustomerId\" = @id", ("a", isActive), ("id", customerId));

    /// <summary>Inserts a vehicle with the plate exactly as given (raw). <c>CreatedAt</c> defaults to 30 days ago.</summary>
    public Task<int> AddVehicleAsync(int ownerCustomerId, string rawPlate, int vehicleTypeId, bool isActive = true, DateTime? createdAtUtc = null)
        => ScalarIntAsync(
            "INSERT INTO \"Vehicles\" (\"LicensePlate\",\"VehicleTypeId\",\"OwnerCustomerId\",\"IsActive\",\"CreatedAt\") " +
            "OUTPUT inserted.\"VehicleId\" VALUES (@p,@vt,@o,@a,@c)",
            ("p", rawPlate), ("vt", vehicleTypeId), ("o", ownerCustomerId), ("a", isActive), ("c", Utc(createdAtUtc ?? DateTime.UtcNow.AddDays(-30))));

    public Task<int> SetVehicleActiveAsync(int vehicleId, bool isActive)
        => _db.ExecuteAsync("UPDATE \"Vehicles\" SET \"IsActive\" = @a WHERE \"VehicleId\" = @id", ("a", isActive), ("id", vehicleId));

    /// <summary>Inserts a monthly ticket bound to <paramref name="vehicleId"/> with <paramref name="registeredRawPlate"/> stored as given.</summary>
    public Task<int> CreateTicketAsync(
        int customerId,
        int vehicleId,
        string registeredRawPlate,
        int vehicleTypeId,
        DateTime startUtc,
        DateTime endUtc,
        MonthlyTicketStatus status = MonthlyTicketStatus.Active,
        int durationMonths = 1,
        decimal monthlyPrice = 100_000m,
        string? ticketCode = null)
        => ScalarIntAsync(
            "INSERT INTO \"MonthlyTickets\" (\"TicketCode\",\"CustomerId\",\"VehicleId\",\"RegisteredLicensePlate\",\"DurationMonths\",\"VehicleTypeId\",\"StartDate\",\"EndDate\",\"MonthlyPrice\",\"Status\",\"CreatedAt\") " +
            "OUTPUT inserted.\"TicketId\" VALUES (@code,@c,@v,@p,@m,@vt,@s,@e,@price,@st,SYSUTCDATETIME())",
            ("code", ticketCode ?? NextCode("MT-T-")), ("c", customerId), ("v", vehicleId), ("p", registeredRawPlate), ("m", durationMonths),
            ("vt", vehicleTypeId), ("s", Utc(startUtc)), ("e", Utc(endUtc)), ("price", monthlyPrice), ("st", (int)status));

    /// <summary>Ticket valid from 10 days ago to 20 days ahead.</summary>
    public Task<int> CreateValidTicketAsync(int customerId, int vehicleId, string registeredRawPlate, int vehicleTypeId, string? ticketCode = null)
    {
        var now = DateTime.UtcNow;
        return CreateTicketAsync(customerId, vehicleId, registeredRawPlate, vehicleTypeId, now.AddDays(-10), now.AddDays(20), ticketCode: ticketCode);
    }

    public Task<string?> TicketCodeAsync(int ticketId)
        => SqlServerDatabaseFixture.ScalarAsync<string>(_db.ConnectionString, "SELECT \"TicketCode\" FROM \"MonthlyTickets\" WHERE \"TicketId\" = @id", ("id", ticketId));

    public Task<int> SetTicketEndAsync(int ticketId, DateTime endUtc)
        => _db.ExecuteAsync("UPDATE \"MonthlyTickets\" SET \"EndDate\" = @e WHERE \"TicketId\" = @id", ("e", Utc(endUtc)), ("id", ticketId));

    public Task<int> SetTicketStatusAsync(int ticketId, MonthlyTicketStatus status)
        => _db.ExecuteAsync("UPDATE \"MonthlyTickets\" SET \"Status\" = @s WHERE \"TicketId\" = @id", ("s", (int)status), ("id", ticketId));

    /// <summary>Inserts a row into the blacklist table with the plate stored as given (raw); returns BlacklistId.</summary>
    public Task<int> AddBlacklistAsync(string rawPlate, string reason = "Test blacklist reason", bool isActive = true, int? customerId = null, string? notes = null)
        => ScalarIntAsync(
            "INSERT INTO \"BlacklistedVehicles\" (\"LicensePlate\",\"Reason\",\"Notes\",\"CustomerId\",\"CreatedAt\",\"IsActive\") " +
            "OUTPUT inserted.\"BlacklistId\" VALUES (@p,@r,@n,@c,SYSUTCDATETIME(),@a)",
            ("p", rawPlate), ("r", reason), ("n", notes), ("c", customerId), ("a", isActive));

    public Task<int> SetBlacklistActiveAsync(int blacklistId, bool isActive)
        => _db.ExecuteAsync("UPDATE \"BlacklistedVehicles\" SET \"IsActive\" = @a WHERE \"BlacklistId\" = @id", ("a", isActive), ("id", blacklistId));

    // ---- convenience builders -------------------------------------------------------------------------------------

    /// <summary>
    /// Resident (D1): customer in an ACTIVE household, one active vehicle stored with a raw plate; optionally a valid ticket.
    /// <c>Customer.Type</c> is written as External on purpose, to prove that residency does not come from the type.
    /// </summary>
    public async Task<TestOwner> CreateResidentAsync(
        int vehicleTypeId,
        bool withTicket = false,
        string? apartment = null,
        string? normalizedPlate = null,
        bool customerActive = true,
        bool householdActive = true,
        CustomerType type = CustomerType.External,
        int? ticketVehicleTypeId = null)
    {
        apartment ??= UniqueApartment();
        var householdId = await CreateHouseholdAsync(apartment, householdActive);
        var customerId = await CreateCustomerAsync(householdId, type, customerActive);
        return await CompleteOwnerAsync(customerId, householdId, apartment, vehicleTypeId, withTicket, normalizedPlate, ticketVehicleTypeId);
    }

    /// <summary>Non-resident (no household) with one active vehicle stored with a raw plate; by default with a valid ticket.</summary>
    public async Task<TestOwner> CreateExternalAsync(
        int vehicleTypeId,
        bool withTicket = true,
        string? normalizedPlate = null,
        bool customerActive = true,
        CustomerType type = CustomerType.External,
        int? ticketVehicleTypeId = null)
    {
        var customerId = await CreateCustomerAsync(null, type, customerActive);
        return await CompleteOwnerAsync(customerId, null, null, vehicleTypeId, withTicket, normalizedPlate, ticketVehicleTypeId);
    }

    private async Task<TestOwner> CompleteOwnerAsync(int customerId, int? householdId, string? apartment, int vehicleTypeId, bool withTicket, string? normalizedPlate, int? ticketVehicleTypeId)
    {
        var norm = normalizedPlate ?? UniqueNormalizedPlate();
        var raw = Raw(norm);
        var vehicleId = await AddVehicleAsync(customerId, raw, vehicleTypeId);
        int? ticketId = null;
        string? ticketCode = null;
        if (withTicket)
        {
            ticketCode = NextCode("MT-T-");
            ticketId = await CreateValidTicketAsync(customerId, vehicleId, raw, ticketVehicleTypeId ?? vehicleTypeId, ticketCode);
        }

        return new TestOwner(customerId, householdId, apartment, vehicleId, vehicleTypeId, raw, norm, ticketId, ticketCode);
    }

    // ---- sessions -------------------------------------------------------------------------------------------------

    public Task<int> SetSessionCheckInAsync(int sessionId, DateTime checkInUtc)
        => _db.ExecuteAsync("UPDATE \"ParkingSessions\" SET \"CheckInTime\" = @t WHERE \"SessionId\" = @id", ("t", Utc(checkInUtc)), ("id", sessionId));

    public Task<DateTime> SessionCheckInAsync(int sessionId)
        => SqlServerDatabaseFixture.ScalarAsync<DateTime>(_db.ConnectionString, "SELECT \"CheckInTime\" FROM \"ParkingSessions\" WHERE \"SessionId\" = @id", ("id", sessionId));

    // ---- read helpers ---------------------------------------------------------------------------------------------

    public Task<long> CountAsync(string sql, params (string Name, object? Value)[] parameters)
        => SqlServerDatabaseFixture.ScalarAsync<long>(_db.ConnectionString, sql, parameters);

    /// <summary>Number of ParkingSessions rows (any status) whose normalized plate equals <paramref name="normalizedPlate"/>.</summary>
    public Task<long> SessionCountForPlateAsync(string normalizedPlate, bool activeOnly = false)
        => CountAsync(
            "SELECT count(*) FROM \"ParkingSessions\" WHERE dbo.fn_NormalizePlate(\"LicensePlate\") = @p" +
            (activeOnly ? " AND \"Status\" = 0" : string.Empty),
            ("p", normalizedPlate));

    public Task<long> ActiveSessionCountForVehicleTypeAsync(int vehicleTypeId)
        => CountAsync("SELECT count(*) FROM \"ParkingSessions\" WHERE \"VehicleTypeId\" = @vt AND \"Status\" = 0", ("vt", vehicleTypeId));

    public Task<long> OccupiedSlotCountAsync()
        => CountAsync("SELECT count(*) FROM \"ParkingSlots\" WHERE \"Status\" <> 0");

    public Task<long> OccupiedSlotCountInZoneAsync(int zoneId)
        => CountAsync("SELECT count(*) FROM \"ParkingSlots\" WHERE \"ZoneId\" = @z AND \"Status\" <> 0", ("z", zoneId));

    public async Task<SlotStatus> SlotStatusAsync(int slotId)
        => (SlotStatus)await ScalarIntAsync("SELECT \"Status\" FROM \"ParkingSlots\" WHERE \"SlotId\" = @id", ("id", slotId));

    public Task<string?> SlotPlateAsync(int slotId)
        => SqlServerDatabaseFixture.ScalarAsync<string>(_db.ConnectionString, "SELECT \"CurrentLicensePlate\" FROM \"ParkingSlots\" WHERE \"SlotId\" = @id", ("id", slotId));

    /// <summary>Audience of the zone that holds the slot (Mixed when the slot has no zone).</summary>
    public async Task<ZoneAudience> SlotAudienceAsync(int slotId)
        => (ZoneAudience)await ScalarIntAsync(
            "SELECT COALESCE(z.\"Audience\", 0) FROM \"ParkingSlots\" s LEFT JOIN \"ParkingZones\" z ON z.\"ZoneId\" = s.\"ZoneId\" WHERE s.\"SlotId\" = @id",
            ("id", slotId));

    public Task<string?> SlotZoneCodeAsync(int slotId)
        => SqlServerDatabaseFixture.ScalarAsync<string>(_db.ConnectionString,
            "SELECT z.\"ZoneCode\" FROM \"ParkingSlots\" s LEFT JOIN \"ParkingZones\" z ON z.\"ZoneId\" = s.\"ZoneId\" WHERE s.\"SlotId\" = @id",
            ("id", slotId));

    private async Task<int> ScalarIntAsync(string sql, params (string Name, object? Value)[] parameters)
        => await SqlServerDatabaseFixture.ScalarAsync<int>(_db.ConnectionString, sql, parameters);

    public static DateTime Utc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
}
