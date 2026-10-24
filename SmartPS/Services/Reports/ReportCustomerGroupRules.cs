using SmartPS.Models.Parking;
using SmartPS.Models.Reports;

namespace SmartPS.Services.Reports;

/// <summary>
/// Customer groups over sessions, following the household model written by the gate: a session is Resident when
/// <c>CustomerType = Resident</c> (the vehicle owner belongs to an active household, with or without a monthly ticket);
/// a non-resident (<c>CustomerType = External</c>) with <c>IsMonthlyPass</c> is a monthly-pass holder; every other
/// non-resident session is a visitor.
/// </summary>
public static class ReportCustomerGroupRules
{
    public static int ResidentCustomerTypeValue => (int)CustomerType.Resident;

    /// <summary>SQL expression yielding the <see cref="ReportCustomerGroup"/> value (needs the <c>@res</c> parameter and alias <c>s</c>).</summary>
    public const string SqlGroupCase =
        "CASE WHEN s.\"CustomerType\" = @res THEN 1 WHEN s.\"IsMonthlyPass\" = 1 THEN 2 ELSE 3 END";

    public static ReportCustomerGroup Classify(CustomerType type, bool isMonthlyPass)
    {
        if (type == CustomerType.Resident)
        {
            return ReportCustomerGroup.Resident;
        }

        return isMonthlyPass ? ReportCustomerGroup.MonthlyPass : ReportCustomerGroup.Visitor;
    }

    public static IQueryable<ParkingSession> Apply(IQueryable<ParkingSession> query, ReportCustomerGroup group) => group switch
    {
        ReportCustomerGroup.Resident => query.Where(s => s.CustomerType == CustomerType.Resident),
        ReportCustomerGroup.MonthlyPass => query.Where(s => s.IsMonthlyPass && s.CustomerType != CustomerType.Resident),
        ReportCustomerGroup.Visitor => query.Where(s => !s.IsMonthlyPass && s.CustomerType != CustomerType.Resident),
        _ => query
    };
}
