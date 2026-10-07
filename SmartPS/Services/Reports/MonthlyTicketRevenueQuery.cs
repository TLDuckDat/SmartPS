using Microsoft.EntityFrameworkCore;
using SmartPS.Data;
using SmartPS.Models.Parking;
using SmartPS.Models.Reports;

namespace SmartPS.Services.Reports;

/// <summary>
/// Monthly-ticket revenue (spec section 6.1): sum of <c>MonthlyTicketPurchases.Price</c> created in the period, split into
/// new and renewed tickets. This is the only query that reads Task 1's purchase table; parking revenue is untouched.
/// </summary>
internal static class MonthlyTicketRevenueQuery
{
    /// <returns>Null when a zone filter is set (not applicable); zeros for the Visitor group, which has no tickets.</returns>
    public static async Task<MonthlyTicketSales?> ExecuteAsync(
        SmartPsDbContext db, DateTime fromUtc, DateTime toUtc, ReportFilter filter, CancellationToken ct)
    {
        if (filter.ZoneId.HasValue)
        {
            return null;
        }

        if (filter.CustomerGroup == ReportCustomerGroup.Visitor)
        {
            return MonthlyTicketSales.Empty;
        }

        var rows = await db.Database
            .SqlQueryRaw<TicketKindRow>(ReportSql.MonthlyTicketSalesByKind, ReportSqlParameters.Create(fromUtc, toUtc, filter))
            .ToListAsync(ct);

        var created = rows.Where(r => r.Kind == (int)TicketPurchaseKind.Create).ToList();
        var renewed = rows.Where(r => r.Kind == (int)TicketPurchaseKind.Renew).ToList();
        return new MonthlyTicketSales(
            created.Sum(r => r.Amount), created.Sum(r => r.Count),
            renewed.Sum(r => r.Amount), renewed.Sum(r => r.Count));
    }
}
