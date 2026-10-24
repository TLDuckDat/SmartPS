namespace SmartPS.Services.Reports;

/// <summary>
/// Constant SQL text (T-SQL) for the report aggregates. Optional filters use the <c>(@x IS NULL OR ...)</c> pattern so the
/// text never changes; values travel as typed parameters (<see cref="ReportSqlParameters"/>). Every selected column has an
/// explicit <c>AS "Name"</c> alias matching the row class in <see cref="ReportSqlRows"/>.
/// T-SQL cannot group by column position, so every GROUP BY repeats its expression.
/// </summary>
internal static class ReportSql
{
    /// <summary>Fixed UTC+7 (must equal <c>AuditTime.VietnamOffset</c>; asserted by a unit test).</summary>
    public const int VietnamOffsetHours = 7;

    private static readonly string VietnamOffset = VietnamOffsetHours.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Session predicate; needs aliases <c>s</c> (ParkingSessions) and <c>sl</c> (ParkingSlots, LEFT JOIN).</summary>
    public const string SessionFilter =
        "s.\"Status\" <> @cancelled " +
        "AND (@vt IS NULL OR s.\"VehicleTypeId\" = @vt) " +
        "AND (@zone IS NULL OR sl.\"ZoneId\" = @zone) " +
        "AND (@grp = 0 " +
        "OR (@grp = 1 AND s.\"CustomerType\" = @res) " +
        "OR (@grp = 2 AND s.\"IsMonthlyPass\" = 1 AND s.\"CustomerType\" <> @res) " +
        "OR (@grp = 3 AND s.\"IsMonthlyPass\" = 0 AND s.\"CustomerType\" <> @res))";

    private const string SessionFrom =
        "FROM \"ParkingSessions\" s LEFT JOIN \"ParkingSlots\" sl ON sl.\"SlotId\" = s.\"SlotId\" ";

    // Đầu giờ UTC chứa thời điểm (thay date_trunc('hour', ...)); dùng được trên mọi phiên bản SQL Server
    private static string HourStart(string column) => "DATEADD(hour, DATEDIFF(hour, 0, " + column + "), 0)";

    // Số giây (độ chính xác micro giây) giữa hai thời điểm (thay EXTRACT(EPOCH FROM b - a))
    private const string DurationSeconds =
        "CAST(COALESCE(SUM(CAST(DATEDIFF_BIG(microsecond, s.\"CheckInTime\", s.\"CheckOutTime\") AS float)), 0) / 1000000.0 AS float)";

    /// <summary>Q1: check-ins per UTC hour.</summary>
    public static readonly string CheckInsByHour =
        "SELECT " + HourStart("s.\"CheckInTime\"") + " AS \"HourUtc\", COUNT(*) AS \"Count\" " +
        SessionFrom +
        "WHERE s.\"CheckInTime\" >= @from AND s.\"CheckInTime\" < @to AND " + SessionFilter + " " +
        "GROUP BY " + HourStart("s.\"CheckInTime\"");

    /// <summary>Q2: completed check-outs per UTC hour with free count and duration seconds.</summary>
    public static readonly string CheckOutsByHour =
        "SELECT " + HourStart("s.\"CheckOutTime\"") + " AS \"HourUtc\", COUNT(*) AS \"Count\", " +
        "COALESCE(SUM(CASE WHEN s.\"TotalFee\" = 0 OR s.\"PaymentMethod\" = @free THEN 1 ELSE 0 END), 0) AS \"FreeCount\", " +
        DurationSeconds + " AS \"DurationSeconds\" " +
        SessionFrom +
        "WHERE s.\"Status\" = @completed AND s.\"CheckOutTime\" >= @from AND s.\"CheckOutTime\" < @to AND " + SessionFilter + " " +
        "GROUP BY " + HourStart("s.\"CheckOutTime\"");

    /// <summary>Q3: check-ins per vehicle type and customer group.</summary>
    public static readonly string CheckInsByTypeAndGroup =
        "SELECT s.\"VehicleTypeId\" AS \"VehicleTypeId\", " + ReportCustomerGroupRules.SqlGroupCase + " AS \"Group\", COUNT(*) AS \"Count\" " +
        SessionFrom +
        "WHERE s.\"CheckInTime\" >= @from AND s.\"CheckInTime\" < @to AND " + SessionFilter + " " +
        "GROUP BY s.\"VehicleTypeId\", " + ReportCustomerGroupRules.SqlGroupCase;

    /// <summary>Q4: completed check-outs per vehicle type.</summary>
    public const string CheckOutsByType =
        "SELECT s.\"VehicleTypeId\" AS \"VehicleTypeId\", COUNT(*) AS \"Count\", " +
        DurationSeconds + " AS \"DurationSeconds\" " +
        SessionFrom +
        "WHERE s.\"Status\" = @completed AND s.\"CheckOutTime\" >= @from AND s.\"CheckOutTime\" < @to AND " + SessionFilter + " " +
        "GROUP BY s.\"VehicleTypeId\"";

    private const string FinancialFrom =
        "FROM \"FinancialTransactions\" f " +
        "LEFT JOIN \"ParkingSessions\" s ON s.\"SessionId\" = f.\"ParkingSessionId\" " +
        "LEFT JOIN \"ParkingSlots\" sl ON sl.\"SlotId\" = s.\"SlotId\" " +
        "WHERE f.\"CreatedAt\" >= @from AND f.\"CreatedAt\" < @to " +
        "AND f.\"Type\" IN (@fee, @refund, @adjust) " +
        "AND (@sessionFiltered = 0 OR (s.\"SessionId\" IS NOT NULL AND " + SessionFilter + ")) ";

    // Ngày theo giờ Việt Nam của thời điểm UTC
    private static readonly string FinancialDayVn = "CAST(DATEADD(hour, " + VietnamOffset + ", f.\"CreatedAt\") AS date)";

    /// <summary>Q5: money per Vietnam day, type and payment method.</summary>
    public static readonly string FinancialsByDay =
        "SELECT " + FinancialDayVn + " AS \"DayVn\", " +
        "f.\"Type\" AS \"Type\", f.\"PaymentMethod\" AS \"Method\", COALESCE(SUM(f.\"Amount\"), 0) AS \"Amount\" " +
        FinancialFrom +
        "GROUP BY " + FinancialDayVn + ", f.\"Type\", f.\"PaymentMethod\"";

    /// <summary>Q6: net money per vehicle type (session-linked transactions only).</summary>
    public const string FinancialsByType =
        "SELECT s.\"VehicleTypeId\" AS \"VehicleTypeId\", COALESCE(SUM(f.\"Amount\"), 0) AS \"Amount\" " +
        FinancialFrom +
        "AND s.\"SessionId\" IS NOT NULL " +
        "GROUP BY s.\"VehicleTypeId\"";

    /// <summary>Q7: vehicles already parked when the period starts.</summary>
    public const string InitialOccupancy =
        "SELECT COUNT(*) AS \"Value\" " +
        SessionFrom +
        "WHERE s.\"CheckInTime\" < @from AND (s.\"CheckOutTime\" IS NULL OR s.\"CheckOutTime\" >= @from) AND " + SessionFilter;

    /// <summary>Q8: slots matching the vehicle-type and zone filters, any status.</summary>
    public const string Capacity =
        "SELECT COUNT(*) AS \"Value\" FROM \"ParkingSlots\" sl " +
        "WHERE (@vt IS NULL OR sl.\"VehicleTypeId\" = @vt) AND (@zone IS NULL OR sl.\"ZoneId\" = @zone)";

    /// <summary>Q9: vehicles in the lot now.</summary>
    public const string ActiveNow =
        "SELECT COUNT(*) AS \"Value\" " +
        SessionFrom +
        "WHERE s.\"Status\" = @active AND " + SessionFilter;

    /// <summary>Q10a: slots per zone.</summary>
    public const string SlotsPerZone =
        "SELECT z.\"ZoneId\" AS \"ZoneId\", z.\"ZoneName\" AS \"ZoneName\", COUNT(sl.\"SlotId\") AS \"Count\" " +
        "FROM \"ParkingZones\" z LEFT JOIN \"ParkingSlots\" sl ON sl.\"ZoneId\" = z.\"ZoneId\" " +
        "AND (@vt IS NULL OR sl.\"VehicleTypeId\" = @vt) " +
        "WHERE (@zone IS NULL OR z.\"ZoneId\" = @zone) " +
        "GROUP BY z.\"ZoneId\", z.\"ZoneName\", z.\"ZoneCode\" " +
        "HAVING @vt IS NULL OR COUNT(sl.\"SlotId\") > 0 " +
        "ORDER BY z.\"ZoneCode\", z.\"ZoneId\"";

    /// <summary>Q10b: active sessions per zone.</summary>
    public const string ActivePerZone =
        "SELECT sl.\"ZoneId\" AS \"ZoneId\", COUNT(*) AS \"Count\" " +
        "FROM \"ParkingSessions\" s JOIN \"ParkingSlots\" sl ON sl.\"SlotId\" = s.\"SlotId\" " +
        "WHERE s.\"Status\" = @active AND sl.\"ZoneId\" IS NOT NULL AND " + SessionFilter + " " +
        "GROUP BY sl.\"ZoneId\"";

    /// <summary>Q11: most frequent plates (normalized like the gate's LicensePlateNormalizer by dbo.fn_NormalizePlate).</summary>
    public static readonly string TopPlates =
        "SELECT TOP (" + ReportLimits.TopPlateCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + ") " +
        "dbo.fn_NormalizePlate(s.\"LicensePlate\") AS \"PlateKey\", " +
        "MIN(s.\"LicensePlate\") AS \"LicensePlate\", COUNT(*) AS \"Visits\", MAX(s.\"CheckInTime\") AS \"LastCheckIn\" " +
        SessionFrom +
        "WHERE s.\"CheckInTime\" >= @from AND s.\"CheckInTime\" < @to AND " + SessionFilter + " " +
        "GROUP BY dbo.fn_NormalizePlate(s.\"LicensePlate\") " +
        "ORDER BY COUNT(*) DESC, dbo.fn_NormalizePlate(s.\"LicensePlate\") COLLATE Latin1_General_BIN2 ASC";
}
