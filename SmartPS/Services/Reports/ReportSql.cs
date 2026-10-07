namespace SmartPS.Services.Reports;

/// <summary>
/// Constant SQL text for the report aggregates. Optional filters use the <c>(@x IS NULL OR ...)</c> pattern so the text never
/// changes; values travel as typed parameters (<see cref="ReportSqlParameters"/>). Every selected column has an explicit
/// <c>AS "Name"</c> alias matching the row class in <see cref="ReportSqlRows"/>.
/// </summary>
internal static class ReportSql
{
    /// <summary>Fixed UTC+7 (must equal <c>AuditTime.VietnamOffset</c>; asserted by a unit test).</summary>
    public const int VietnamOffsetHours = 7;

    private static readonly string VietnamInterval = "INTERVAL '" + VietnamOffsetHours.ToString(System.Globalization.CultureInfo.InvariantCulture) + " hours'";

    /// <summary>Session predicate; needs aliases <c>s</c> (ParkingSessions) and <c>sl</c> (ParkingSlots, LEFT JOIN).</summary>
    public const string SessionFilter =
        "s.\"Status\" <> @cancelled " +
        "AND (@vt IS NULL OR s.\"VehicleTypeId\" = @vt) " +
        "AND (@zone IS NULL OR sl.\"ZoneId\" = @zone) " +
        "AND (@grp = 0 " +
        "OR (@grp = 1 AND s.\"CustomerType\" = @res) " +
        "OR (@grp = 2 AND s.\"IsMonthlyPass\" AND s.\"CustomerType\" <> @res) " +
        "OR (@grp = 3 AND NOT s.\"IsMonthlyPass\" AND s.\"CustomerType\" <> @res))";

    private const string SessionFrom =
        "FROM \"ParkingSessions\" s LEFT JOIN \"ParkingSlots\" sl ON sl.\"SlotId\" = s.\"SlotId\" ";

    /// <summary>Q1: check-ins per UTC hour.</summary>
    public const string CheckInsByHour =
        "SELECT date_trunc('hour', s.\"CheckInTime\" AT TIME ZONE 'UTC') AS \"HourUtc\", COUNT(*)::int AS \"Count\" " +
        SessionFrom +
        "WHERE s.\"CheckInTime\" >= @from AND s.\"CheckInTime\" < @to AND " + SessionFilter + " " +
        "GROUP BY 1";

    /// <summary>Q2: completed check-outs per UTC hour with free count and duration seconds.</summary>
    public const string CheckOutsByHour =
        "SELECT date_trunc('hour', s.\"CheckOutTime\" AT TIME ZONE 'UTC') AS \"HourUtc\", COUNT(*)::int AS \"Count\", " +
        "COALESCE(SUM(CASE WHEN s.\"TotalFee\" = 0 OR s.\"PaymentMethod\" = @free THEN 1 ELSE 0 END), 0)::int AS \"FreeCount\", " +
        "COALESCE(SUM(EXTRACT(EPOCH FROM (s.\"CheckOutTime\" - s.\"CheckInTime\"))), 0)::float8 AS \"DurationSeconds\" " +
        SessionFrom +
        "WHERE s.\"Status\" = @completed AND s.\"CheckOutTime\" >= @from AND s.\"CheckOutTime\" < @to AND " + SessionFilter + " " +
        "GROUP BY 1";

    /// <summary>Q3: check-ins per vehicle type and customer group.</summary>
    public static readonly string CheckInsByTypeAndGroup =
        "SELECT s.\"VehicleTypeId\" AS \"VehicleTypeId\", " + ReportCustomerGroupRules.SqlGroupCase + " AS \"Group\", COUNT(*)::int AS \"Count\" " +
        SessionFrom +
        "WHERE s.\"CheckInTime\" >= @from AND s.\"CheckInTime\" < @to AND " + SessionFilter + " " +
        "GROUP BY 1, 2";

    /// <summary>Q4: completed check-outs per vehicle type.</summary>
    public const string CheckOutsByType =
        "SELECT s.\"VehicleTypeId\" AS \"VehicleTypeId\", COUNT(*)::int AS \"Count\", " +
        "COALESCE(SUM(EXTRACT(EPOCH FROM (s.\"CheckOutTime\" - s.\"CheckInTime\"))), 0)::float8 AS \"DurationSeconds\" " +
        SessionFrom +
        "WHERE s.\"Status\" = @completed AND s.\"CheckOutTime\" >= @from AND s.\"CheckOutTime\" < @to AND " + SessionFilter + " " +
        "GROUP BY 1";

    private const string FinancialFrom =
        "FROM \"FinancialTransactions\" f " +
        "LEFT JOIN \"ParkingSessions\" s ON s.\"SessionId\" = f.\"ParkingSessionId\" " +
        "LEFT JOIN \"ParkingSlots\" sl ON sl.\"SlotId\" = s.\"SlotId\" " +
        "WHERE f.\"CreatedAt\" >= @from AND f.\"CreatedAt\" < @to " +
        "AND f.\"Type\" IN (@fee, @refund, @adjust) " +
        "AND (NOT @sessionFiltered OR (s.\"SessionId\" IS NOT NULL AND " + SessionFilter + ")) ";

    /// <summary>Q5: money per Vietnam day, type and payment method.</summary>
    public static readonly string FinancialsByDay =
        "SELECT ((f.\"CreatedAt\" AT TIME ZONE 'UTC') + " + VietnamInterval + ")::date AS \"DayVn\", " +
        "f.\"Type\" AS \"Type\", f.\"PaymentMethod\" AS \"Method\", COALESCE(SUM(f.\"Amount\"), 0) AS \"Amount\" " +
        FinancialFrom +
        "GROUP BY 1, 2, 3";

    /// <summary>Q6: net money per vehicle type (session-linked transactions only).</summary>
    public const string FinancialsByType =
        "SELECT s.\"VehicleTypeId\" AS \"VehicleTypeId\", COALESCE(SUM(f.\"Amount\"), 0) AS \"Amount\" " +
        FinancialFrom +
        "AND s.\"SessionId\" IS NOT NULL " +
        "GROUP BY 1";

    /// <summary>Q7: vehicles already parked when the period starts.</summary>
    public const string InitialOccupancy =
        "SELECT COUNT(*)::int AS \"Value\" " +
        SessionFrom +
        "WHERE s.\"CheckInTime\" < @from AND (s.\"CheckOutTime\" IS NULL OR s.\"CheckOutTime\" >= @from) AND " + SessionFilter;

    /// <summary>Q8: slots matching the vehicle-type and zone filters, any status.</summary>
    public const string Capacity =
        "SELECT COUNT(*)::int AS \"Value\" FROM \"ParkingSlots\" sl " +
        "WHERE (@vt IS NULL OR sl.\"VehicleTypeId\" = @vt) AND (@zone IS NULL OR sl.\"ZoneId\" = @zone)";

    /// <summary>Q9: vehicles in the lot now.</summary>
    public const string ActiveNow =
        "SELECT COUNT(*)::int AS \"Value\" " +
        SessionFrom +
        "WHERE s.\"Status\" = @active AND " + SessionFilter;

    /// <summary>Q10a: slots per zone.</summary>
    public const string SlotsPerZone =
        "SELECT z.\"ZoneId\" AS \"ZoneId\", z.\"ZoneName\" AS \"ZoneName\", COUNT(sl.\"SlotId\")::int AS \"Count\" " +
        "FROM \"ParkingZones\" z LEFT JOIN \"ParkingSlots\" sl ON sl.\"ZoneId\" = z.\"ZoneId\" " +
        "AND (@vt IS NULL OR sl.\"VehicleTypeId\" = @vt) " +
        "WHERE (@zone IS NULL OR z.\"ZoneId\" = @zone) " +
        "GROUP BY z.\"ZoneId\", z.\"ZoneName\", z.\"ZoneCode\" " +
        "HAVING @vt IS NULL OR COUNT(sl.\"SlotId\") > 0 " +
        "ORDER BY z.\"ZoneCode\", z.\"ZoneId\"";

    /// <summary>Q10b: active sessions per zone.</summary>
    public const string ActivePerZone =
        "SELECT sl.\"ZoneId\" AS \"ZoneId\", COUNT(*)::int AS \"Count\" " +
        "FROM \"ParkingSessions\" s JOIN \"ParkingSlots\" sl ON sl.\"SlotId\" = s.\"SlotId\" " +
        "WHERE s.\"Status\" = @active AND sl.\"ZoneId\" IS NOT NULL AND " + SessionFilter + " " +
        "GROUP BY sl.\"ZoneId\"";

    /// <summary>Q11: most frequent plates (normalized like Task 1's LicensePlateNormalizer).</summary>
    public static readonly string TopPlates =
        "SELECT upper(regexp_replace(s.\"LicensePlate\", '[^A-Za-z0-9]', '', 'g')) AS \"PlateKey\", " +
        "MIN(s.\"LicensePlate\") AS \"LicensePlate\", COUNT(*)::int AS \"Visits\", MAX(s.\"CheckInTime\") AS \"LastCheckIn\" " +
        SessionFrom +
        "WHERE s.\"CheckInTime\" >= @from AND s.\"CheckInTime\" < @to AND " + SessionFilter + " " +
        "GROUP BY 1 ORDER BY 3 DESC, 1 ASC LIMIT " + ReportLimits.TopPlateCount.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Q12: monthly-ticket purchases created or renewed in the period, by kind.</summary>
    public const string MonthlyTicketSalesByKind =
        "SELECT p.\"Kind\" AS \"Kind\", COALESCE(SUM(p.\"Price\"), 0) AS \"Amount\", COUNT(*)::int AS \"Count\" " +
        "FROM \"MonthlyTicketPurchases\" p " +
        "JOIN \"MonthlyTickets\" t ON t.\"TicketId\" = p.\"TicketId\" " +
        "JOIN \"Customers\" c ON c.\"CustomerId\" = t.\"CustomerId\" " +
        "WHERE p.\"CreatedAtUtc\" >= @from AND p.\"CreatedAtUtc\" < @to " +
        "AND (@vt IS NULL OR t.\"VehicleTypeId\" = @vt) " +
        "AND (@grp = 0 OR (@grp = 1 AND c.\"IsResident\") OR (@grp = 2 AND NOT c.\"IsResident\")) " +
        "GROUP BY p.\"Kind\"";
}
