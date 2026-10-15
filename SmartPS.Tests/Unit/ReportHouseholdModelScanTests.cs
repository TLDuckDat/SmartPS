using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SmartPS.Tests.Unit;

/// <summary>
/// AC-R9 / R-C / D5 source and model scans: the monthly-ticket revenue KPI, its Overview rows, its tile and its text keys
/// do not exist anywhere in the reporting code, model, views or dictionaries; reports no longer depend on
/// MonthlyTicketPurchases or Customer.IsResident; the old customer tiers (Regular / Loyal / VIP, Resident = 3) are gone;
/// revenue SQL reads FinancialTransactions and never the pricing table. The zone-not-applicable key is kept (unused is
/// acceptable), and the keys that still serve other figures stay.
/// </summary>
public class ReportHouseholdModelScanTests
{
    private static string Views => Path.Combine(RepoPaths.AppProject, "Views");

    private static IEnumerable<string> ReportingSources() =>
        new[]
            {
                Path.Combine(RepoPaths.Services, "Reports"),
                Path.Combine(RepoPaths.ViewModels, "Reports"),
                Path.Combine(RepoPaths.ViewModels, "Overview"),
                Path.Combine(RepoPaths.AppProject, "Models", "Reports"),
            }
            .SelectMany(RepoPaths.CSharpFiles)
            .Concat(new[] { Path.Combine(Views, "Reports"), Path.Combine(Views, "Overview") }
                .SelectMany(d => Directory.EnumerateFiles(d, "*.*", SearchOption.AllDirectories))
                .Where(f => f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)));

    private static string Rel(string path) => Path.GetRelativePath(RepoPaths.Root, path);

    [Theory]
    [InlineData("MonthlyTicketRevenue")]
    [InlineData("MonthlyTicketSales")]
    [InlineData("MonthlyTicketSplit")]
    [InlineData("MonthlyTicketNew")]
    [InlineData("MonthlyTicketRenew")]
    [InlineData("MonthlyTicketPurchase")]
    [InlineData("TicketPurchaseKind")]
    [InlineData("TicketKindRow")]
    [InlineData("kindCreate")]
    [InlineData("kindRenew")]
    [InlineData("\"MonthlyTickets\"")]
    [InlineData("IsResident")]
    [InlineData("CustomerType.Regular")]
    [InlineData("CustomerType.Loyal")]
    [InlineData("CustomerType.VIP")]
    [InlineData("\"PricingRules\"")]
    [InlineData("MonthlyPrice")]
    public void Reporting_sources_do_not_mention(string token)
    {
        var offenders = ReportingSources()
            .Where(f => File.ReadAllText(f).Contains(token, StringComparison.Ordinal))
            .Select(Rel)
            .ToList();

        Assert.True(offenders.Count == 0, $"'{token}' found in: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Reporting_sources_are_found()
    {
        var files = ReportingSources().Select(Path.GetFileName).ToList();

        Assert.Contains("ReportService.cs", files);
        Assert.Contains("ReportsViewModel.cs", files);
        Assert.Contains("ReportsView.xaml", files);
        Assert.Contains("OverviewView.xaml", files);
        Assert.Contains("ReportKpis.cs", files);
    }

    [Fact]
    public void Removed_monthly_ticket_revenue_files_do_not_exist()
    {
        Assert.False(File.Exists(Path.Combine(RepoPaths.Services, "Reports", "MonthlyTicketRevenueQuery.cs")));
        Assert.False(File.Exists(Path.Combine(RepoPaths.AppProject, "Models", "Reports", "MonthlyTicketSales.cs")));
        Assert.Null(typeof(ReportKpis).Assembly.GetTypes().FirstOrDefault(t => t.Name is "MonthlyTicketSales" or "MonthlyTicketRevenueQuery" or "TicketKindRow"));
    }

    [Theory]
    [InlineData(typeof(ReportKpis))]
    [InlineData(typeof(ReportResult))]
    [InlineData(typeof(PeriodRawData))]
    [InlineData(typeof(PeriodSummary))]
    public void Report_models_have_no_monthly_ticket_member(Type type)
    {
        var members = type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name)
            .Concat(type.GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.Name ?? string.Empty))
            .ToList();

        Assert.NotEmpty(members);
        Assert.DoesNotContain(members, m => m.Contains("MonthlyTicket", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(members, m => m.Contains("Tickets", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Report_text_keys_have_no_monthly_ticket_revenue_key_but_keep_the_other_keys()
    {
        var constants = typeof(ReportTextKeys).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (Name: f.Name, Value: (string)f.GetRawConstantValue()!))
            .ToList();

        Assert.DoesNotContain(constants, c => c.Name.Contains("MonthlyTicket", StringComparison.Ordinal) || c.Value.Contains("MonthlyTicket", StringComparison.Ordinal));
        Assert.DoesNotContain(ReportTextKeys.All, k => k.Contains("MonthlyTicket", StringComparison.Ordinal));

        // kept: zone-not-applicable (unused now, accepted), the session ticket-code column and the MonthlyPass group label
        Assert.Equal("Str_Rpt_Kpi_NotApplicableZone", ReportTextKeys.KpiNotApplicableZone);
        Assert.Contains(ReportTextKeys.KpiNotApplicableZone, ReportTextKeys.All);
        Assert.Contains("Str_Rpt_Col_TicketCode", ReportTextKeys.All);
        Assert.Contains("Str_Rpt_Group_MonthlyPass", ReportTextKeys.All);
        Assert.Contains("Str_Rpt_Group_Resident", ReportTextKeys.All);
        Assert.Contains("Str_Rpt_Group_Visitor", ReportTextKeys.All);
    }

    [Theory]
    [InlineData("vi-VN")]
    [InlineData("en-US")]
    [InlineData("ja-JP")]
    public void Dictionaries_have_no_monthly_ticket_revenue_strings(string culture)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var doc = XDocument.Load(Path.Combine(RepoPaths.Languages, $"Strings.{culture}.xaml"));
        var keys = doc.Root!.Elements().Select(e => (string?)e.Attribute(x + "Key")).Where(k => k is not null).ToList();

        Assert.DoesNotContain(keys, k => k!.StartsWith("Str_Rpt_Kpi_MonthlyTicket", StringComparison.Ordinal));
        Assert.Contains("Str_Rpt_Kpi_NotApplicableZone", keys);
        Assert.Contains("Str_Rpt_Group_MonthlyPass", keys);
        Assert.Contains("Str_Rpt_Col_TicketCode", keys);
    }

    [Fact]
    public void Report_sql_reads_revenue_from_financial_transactions_only()
    {
        var sql = typeof(ReportSql).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(string))
            .ToDictionary(f => f.Name, f => (string)(f.IsLiteral ? f.GetRawConstantValue()! : f.GetValue(null)!));

        Assert.Contains("\"FinancialTransactions\"", sql[nameof(ReportSql.FinancialsByDay)], StringComparison.Ordinal);
        Assert.Contains("\"FinancialTransactions\"", sql[nameof(ReportSql.FinancialsByType)], StringComparison.Ordinal);
        Assert.DoesNotContain(sql, kv => kv.Value.Contains("\"PricingRules\"", StringComparison.Ordinal));
        Assert.DoesNotContain(sql, kv => kv.Value.Contains("\"MonthlyTicket", StringComparison.Ordinal));
        Assert.DoesNotContain(sql, kv => kv.Value.Contains("\"Customers\"", StringComparison.Ordinal));
        Assert.DoesNotContain(sql.Keys, k => k.Contains("MonthlyTicket", StringComparison.Ordinal));
        Assert.DoesNotContain(sql, kv => Regex.IsMatch(kv.Value, @"\bTotalFee\b") && kv.Key.StartsWith("Financials", StringComparison.Ordinal));
    }

    [Fact]
    public void Customer_group_sql_follows_the_session_values()
    {
        // Resident ⇔ CustomerType = @res; MonthlyPass ⇔ IsMonthlyPass AND CustomerType <> @res; Visitor ⇔ NOT IsMonthlyPass AND CustomerType <> @res
        Assert.Contains("(@grp = 1 AND s.\"CustomerType\" = @res)", ReportSql.SessionFilter, StringComparison.Ordinal);
        Assert.Contains("(@grp = 2 AND s.\"IsMonthlyPass\" AND s.\"CustomerType\" <> @res)", ReportSql.SessionFilter, StringComparison.Ordinal);
        Assert.Contains("(@grp = 3 AND NOT s.\"IsMonthlyPass\" AND s.\"CustomerType\" <> @res)", ReportSql.SessionFilter, StringComparison.Ordinal);
        Assert.Equal((int)SmartPS.Models.Parking.CustomerType.Resident, ReportCustomerGroupRules.ResidentCustomerTypeValue);
    }
}
