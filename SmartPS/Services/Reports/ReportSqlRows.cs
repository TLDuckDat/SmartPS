namespace SmartPS.Services.Reports;

// Row types for Database.SqlQueryRaw. Property names match the AS "Name" aliases in ReportSql.

internal sealed class ScalarIntRow
{
    public int Value { get; set; }
}

internal sealed class CheckInHourRow
{
    /// <summary>timestamp without time zone (Kind = Unspecified); callers apply DateTime.SpecifyKind(..., Utc).</summary>
    public DateTime HourUtc { get; set; }

    public int Count { get; set; }
}

internal sealed class CheckOutHourRow
{
    public DateTime HourUtc { get; set; }

    public int Count { get; set; }

    public int FreeCount { get; set; }

    public double DurationSeconds { get; set; }
}

internal sealed class TypeGroupRow
{
    public int VehicleTypeId { get; set; }

    public int Group { get; set; }

    public int Count { get; set; }
}

internal sealed class TypeCheckOutRow
{
    public int VehicleTypeId { get; set; }

    public int Count { get; set; }

    public double DurationSeconds { get; set; }
}

internal sealed class FinancialDayRow
{
    public DateOnly DayVn { get; set; }

    public int Type { get; set; }

    public int Method { get; set; }

    public decimal Amount { get; set; }
}

internal sealed class TypeAmountRow
{
    public int VehicleTypeId { get; set; }

    public decimal Amount { get; set; }
}

internal sealed class ZoneSlotsRow
{
    public int ZoneId { get; set; }

    public string ZoneName { get; set; } = string.Empty;

    public int Count { get; set; }
}

internal sealed class ZoneCountRow
{
    public int ZoneId { get; set; }

    public int Count { get; set; }
}

internal sealed class TopPlateSqlRow
{
    public string PlateKey { get; set; } = string.Empty;

    public string LicensePlate { get; set; } = string.Empty;

    public int Visits { get; set; }

    public DateTime LastCheckIn { get; set; }
}

internal sealed class TicketKindRow
{
    public int Kind { get; set; }

    public decimal Amount { get; set; }

    public int Count { get; set; }
}
