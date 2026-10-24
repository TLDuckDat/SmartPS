using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SmartPS.Tests.Unit;

/// <summary>
/// AC-R9 / R-C / D5 source and model scans: the monthly-ticket revenue KPI, its Overview rows, its tile and its text keys
/// do not exist anywhere in the reporting code, model, views or dictionaries; reports no longer depend on
/// MonthlyTicketPurchases or Customer.IsResident; the old customer tiers (Regular / Loyal / VIP, Resident = 3) are gone;
/// revenue SQL reads FinancialTransactions and never the pricing table. Review r1 m1: the zone-not-applicable key, which only
/// served the removed tile, is gone too (constant, All entry and the three dictionaries); the keys that still serve other
/// figures stay.
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
    [InlineData("NotApplicableZone")]
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

        // r1 m1: the zone-not-applicable key served only the removed monthly tile; constant and All entry are gone
        Assert.DoesNotContain(constants, c => c.Name == "KpiNotApplicableZone" || c.Value == "Str_Rpt_Kpi_NotApplicableZone");
        Assert.DoesNotContain("Str_Rpt_Kpi_NotApplicableZone", ReportTextKeys.All);
        Assert.DoesNotContain(ReportTextKeys.All, k => k.Contains("NotApplicable", StringComparison.Ordinal));

        // kept: the session ticket-code column and the group labels
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
        Assert.DoesNotContain("Str_Rpt_Kpi_NotApplicableZone", keys); // r1 m1
        Assert.DoesNotContain(keys, k => k!.Contains("NotApplicable", StringComparison.Ordinal));
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
        Assert.Contains("(@grp = 2 AND s.\"IsMonthlyPass\" = 1 AND s.\"CustomerType\" <> @res)", ReportSql.SessionFilter, StringComparison.Ordinal);
        Assert.Contains("(@grp = 3 AND s.\"IsMonthlyPass\" = 0 AND s.\"CustomerType\" <> @res)", ReportSql.SessionFilter, StringComparison.Ordinal);
        Assert.Equal((int)SmartPS.Models.Parking.CustomerType.Resident, ReportCustomerGroupRules.ResidentCustomerTypeValue);
    }

    // ---- review r1 m1: no source file anywhere in the app mentions the zone-not-applicable key -------------------------

    [Fact]
    public void No_app_source_or_dictionary_mentions_the_zone_not_applicable_key()
    {
        var offenders = Directory.EnumerateFiles(RepoPaths.AppProject, "*.*", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            .Where(f => File.ReadAllText(f).Contains("NotApplicableZone", StringComparison.Ordinal))
            .Select(Rel)
            .ToList();

        Assert.True(offenders.Count == 0, $"'NotApplicableZone' found in: {string.Join(", ", offenders)}");
    }

    // ---- review r1 m2: the recent-sessions query does not load the customer ----------------------------------------------

    [Fact]
    public void Recent_sessions_query_does_not_include_the_customer()
    {
        var text = File.ReadAllText(Path.Combine(RepoPaths.Services, "Reports", "ReportService.cs"));
        var start = text.IndexOf("GetRecentSessionsAsync(", StringComparison.Ordinal);
        Assert.True(start >= 0, "GetRecentSessionsAsync not found");
        var end = text.IndexOf("ToListAsync", start, StringComparison.Ordinal);
        Assert.True(end > start, "GetRecentSessionsAsync body not found");
        var body = text[start..end];

        Assert.DoesNotMatch(new Regex(@"\.Include\(\s*\w+\s*=>\s*\w+\.Customer\s*\)"), body);
        Assert.DoesNotContain("Customer", body, StringComparison.Ordinal);
    }

    // ---- review r1 m3: Overview visitor count is named VisitorParkedCount (VM and XAML binding) ---------------------------

    [Fact]
    public void Overview_visitor_count_is_named_VisitorParkedCount_in_the_view_model_and_the_view()
    {
        var vmType = typeof(SmartPS.ViewModels.Overview.OverviewViewModel);
        Assert.NotNull(vmType.GetProperty("VisitorParkedCount", BindingFlags.Public | BindingFlags.Instance));
        Assert.Null(vmType.GetProperty("RegularParkedCount", BindingFlags.Public | BindingFlags.Instance));

        var vmText = File.ReadAllText(Path.Combine(RepoPaths.ViewModels, "Overview", "OverviewViewModel.cs"));
        var xaml = File.ReadAllText(Path.Combine(Views, "Overview", "OverviewView.xaml"));
        Assert.DoesNotContain("RegularParked", vmText, StringComparison.Ordinal);
        Assert.DoesNotContain("RegularParked", xaml, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"\{Binding\s+(Path=)?VisitorParkedCount\b"), xaml);
    }
}
