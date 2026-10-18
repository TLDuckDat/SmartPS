using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SmartPS.Tests.Unit;

/// <summary>
/// Parked vehicles list (DataGrid bound to ActiveSessions) on the gate screen: the badge in the "Khách Hàng" column must
/// follow the stored ParkingSession.CustomerType. Rule: CustomerType == Resident -> "Cư dân" (wins over a monthly pass);
/// else IsMonthlyPass -> "👑 Vé tháng"; else "Vãng lai". WPF applies later triggers last, so the Resident trigger must
/// come after the IsMonthlyPass trigger in both the TextBlock style and the Border style.
/// </summary>
public class ParkedListCustomerBadgeTests
{
    private static readonly XNamespace P = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static readonly Regex MonthlyPassBinding = new(@"^\{Binding\s+(Path=)?IsMonthlyPass\s*\}$");
    private static readonly Regex CustomerTypeBinding = new(@"^\{Binding\s+(Path=)?CustomerType\s*\}$");

    private static string ViewPath => Path.Combine(RepoPaths.AppProject, "Views", "GateControl", "GateControlView.xaml");

    private static XElement CustomerBadgeBorder()
    {
        Assert.True(File.Exists(ViewPath), ViewPath);
        var doc = XDocument.Load(ViewPath);

        var grid = doc.Descendants(P + "DataGrid")
            .Single(g => ((string?)g.Attribute("ItemsSource"))?.Replace(" ", "") == "{BindingActiveSessions}");

        var column = grid.Descendants(P + "DataGridTemplateColumn")
            .Single(c => (string?)c.Attribute("Header") == "Khách Hàng");

        return column.Descendants(P + "Border")
            .Single(b => b.Element(P + "Border.Style") is not null);
    }

    private static List<XElement> Triggers(XElement style) =>
        style.Element(P + "Style.Triggers")?.Elements().ToList() ?? new List<XElement>();

    private static string? SetterValue(XElement owner, string property) =>
        owner.Elements(P + "Setter")
            .Where(s => (string?)s.Attribute("Property") == property)
            .Select(s => (string?)s.Attribute("Value"))
            .LastOrDefault();

    private static bool IsResidentValue(string? value) =>
        value == "Resident" || (value is not null && Regex.IsMatch(value, @"^\{x:Static\s+[\w:.]*CustomerType\.Resident\s*\}$"));

    private static int IndexOf(List<XElement> triggers, Regex binding, Func<string?, bool> value) =>
        triggers.FindIndex(t => t.Name == P + "DataTrigger"
                                && binding.IsMatch((string?)t.Attribute("Binding") ?? string.Empty)
                                && value((string?)t.Attribute("Value")));

    [Fact]
    public void Parked_list_badge_shows_resident_for_household_members()
    {
        var border = CustomerBadgeBorder();

        // ---- TextBlock style (badge text) --------------------------------------------------------------------------
        var textStyle = border.Descendants(P + "TextBlock.Style").Single().Element(P + "Style")!;
        Assert.Equal("Vãng lai", SetterValue(textStyle, "Text"));

        var textTriggers = Triggers(textStyle);
        var monthlyText = IndexOf(textTriggers, MonthlyPassBinding, v => v == "True");
        Assert.True(monthlyText >= 0, "TextBlock style must keep the IsMonthlyPass=True DataTrigger");
        Assert.Equal("👑 Vé tháng", SetterValue(textTriggers[monthlyText], "Text"));

        var residentText = IndexOf(textTriggers, CustomerTypeBinding, IsResidentValue);
        Assert.True(residentText >= 0, "TextBlock style must have a DataTrigger Binding=\"{Binding CustomerType}\" Value=\"Resident\"");
        Assert.Contains("Cư dân", SetterValue(textTriggers[residentText], "Text") ?? string.Empty, StringComparison.Ordinal);
        Assert.True(residentText > monthlyText, "The Resident trigger must come after the IsMonthlyPass trigger (resident wins)");

        // ---- Border style (badge background) -----------------------------------------------------------------------
        var borderStyle = border.Element(P + "Border.Style")!.Element(P + "Style")!;
        var borderTriggers = Triggers(borderStyle);
        var monthlyBorder = IndexOf(borderTriggers, MonthlyPassBinding, v => v == "True");
        Assert.True(monthlyBorder >= 0, "Border style must keep the IsMonthlyPass=True DataTrigger");
        var monthlyBackground = SetterValue(borderTriggers[monthlyBorder], "Background");

        var residentBorder = IndexOf(borderTriggers, CustomerTypeBinding, IsResidentValue);
        Assert.True(residentBorder >= 0, "Border style must have a DataTrigger Binding=\"{Binding CustomerType}\" Value=\"Resident\"");
        Assert.True(residentBorder > monthlyBorder, "The Resident Border trigger must come after the IsMonthlyPass trigger");

        var residentBackground = SetterValue(borderTriggers[residentBorder], "Background");
        Assert.False(string.IsNullOrWhiteSpace(residentBackground), "The Resident Border trigger must set Background");
        Assert.NotEqual(monthlyBackground, residentBackground, StringComparer.OrdinalIgnoreCase);
        Assert.NotEqual(SetterValue(borderStyle, "Background"), residentBackground, StringComparer.OrdinalIgnoreCase);
    }
}
