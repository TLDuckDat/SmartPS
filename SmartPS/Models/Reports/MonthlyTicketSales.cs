namespace SmartPS.Models.Reports;

/// <summary>Monthly-ticket purchases created or renewed in a period (spec section 6.1), split by kind.</summary>
public sealed record MonthlyTicketSales(decimal NewRevenue, int NewCount, decimal RenewRevenue, int RenewCount)
{
    public static MonthlyTicketSales Empty { get; } = new(0m, 0, 0m, 0);

    public decimal TotalRevenue => NewRevenue + RenewRevenue;

    public int TotalCount => NewCount + RenewCount;
}
