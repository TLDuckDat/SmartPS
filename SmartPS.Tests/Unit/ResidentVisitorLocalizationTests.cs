using System.Xml.Linq;
using SmartPS.Models.Parking;
using SmartPS.Services.Common;

namespace SmartPS.Tests.Unit;

/// <summary>
/// B5 / plan R-D: every key of the gate flow (badges, reject messages, parking map by zone) exists and is non-empty in
/// the 3 language files; the keys of the removed customer/ticket/blacklist screens are gone.
/// </summary>
public class ResidentVisitorLocalizationTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly string[] Cultures = { "vi-VN", "en-US", "ja-JP" };

    private static Dictionary<string, string> Entries(string culture)
    {
        var path = Path.Combine(RepoPaths.Languages, $"Strings.{culture}.xaml");
        Assert.True(File.Exists(path), path);
        return XDocument.Load(path).Root!.Elements()
            .Select(e => (Key: (string?)e.Attribute(X + "Key"), e.Value))
            .Where(e => e.Key is not null)
            .GroupBy(e => e.Key!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);
    }

    public static IEnumerable<string> RequiredKeys()
    {
        var keys = new List<string>();

        // Gate badges, groups and messages
        keys.AddRange(new[] { "Visitor", "Resident", "MonthlyPass", "Blacklisted", "CustomerLocked", "TicketVehicleTypeMismatch", "TicketUntil" }
            .Select(s => $"Str_Gate_Badge_{s}"));
        keys.AddRange(new[] { "Resident", "Visitor", "MonthlyPass" }.Select(s => $"Str_Gate_Group_{s}"));
        keys.AddRange(new[]
        {
            "BlacklistBlocked", "NoSlotFor", "SlotNotFound", "SlotVehicleTypeMismatch", "SlotAudienceNotAllowed", "SlotNotAvailable",
            "CheckInResident", "CheckInMonthly", "CheckInVisitor", "PlateInvalid", "ClassificationFailed",
            // Fix round 2, H6: every check-in rejection is localized
            "AlreadyInside", "EmptyPlate", "CheckInFailed"
        }.Select(s => $"Msg_Gate_{s}"));

        // Operation errors used by the zone service
        keys.AddRange(Enum.GetNames<OperationError>().Where(n => n != nameof(OperationError.None)).Select(s => $"Msg_Op_{s}"));

        // Parking map by zone
        keys.AddRange(Enum.GetNames<ZoneAudience>().Select(s => $"Str_Map_Audience_{s}"));
        keys.AddRange(new[] { "ZoneSummary", "SaveAudience", "Unzoned", "MaintenanceCount" }.Select(s => $"Str_Map_{s}"));
        keys.AddRange(new[] { "Available", "Occupied", "Maintenance" }.Select(s => $"Str_Map_Status_{s}"));
        keys.AddRange(new[] { "AudienceSaved", "AudienceSaveError" }.Select(s => $"Msg_Map_{s}"));

        return keys;
    }

    [Fact]
    public void Enum_driven_key_sets_have_the_expected_sizes()
    {
        Assert.Equal(4, Enum.GetNames<OperationError>().Count(n => n != nameof(OperationError.None)));
        Assert.Equal(3, Enum.GetNames<ZoneAudience>().Length);
    }

    [Fact]
    public void Every_gate_flow_key_exists_and_is_non_empty_in_all_languages()
    {
        var required = RequiredKeys().Distinct(StringComparer.Ordinal).ToList();

        foreach (var culture in Cultures)
        {
            var entries = Entries(culture);
            var missing = required.Where(k => !entries.ContainsKey(k)).ToList();
            Assert.True(missing.Count == 0, $"{culture} is missing: {string.Join(", ", missing)}");
            var empty = required.Where(k => string.IsNullOrWhiteSpace(entries[k])).ToList();
            Assert.True(empty.Count == 0, $"{culture} has empty values for: {string.Join(", ", empty)}");
        }
    }

    [Fact]
    public void Ticket_until_badge_suffix_takes_the_date_placeholder()
    {
        foreach (var culture in Cultures)
        {
            Assert.Contains("{0}", Entries(culture)["Str_Gate_Badge_TicketUntil"], StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("Str_Perm_Module_Customer")]
    [InlineData("Str_Perm_Module_Blacklist")]
    [InlineData("Str_Perm_Customer_")]
    [InlineData("Str_Perm_Blacklist_")]
    [InlineData("Str_Cust_")]
    [InlineData("Str_CustType_")]
    [InlineData("Str_TicketStatus_")]
    [InlineData("Str_Blacklist_")]
    [InlineData("Msg_Cust_")]
    [InlineData("Msg_Blacklist_")]
    [InlineData("Msg_CustomerValidation_")]
    [InlineData("Msg_Gate_TicketExpiredDuringStay")]
    [InlineData("Msg_Gate_TicketNoLongerValid")]
    [InlineData("Msg_Gate_BlacklistExitWarning")]
    public void Keys_of_removed_features_are_gone_from_all_languages(string prefix)
    {
        foreach (var culture in Cultures)
        {
            var stale = Entries(culture).Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            Assert.True(stale.Count == 0, $"{culture} still has: {string.Join(", ", stale)}");
        }
    }

    [Fact]
    public void Only_the_kept_operation_error_keys_remain()
    {
        var expected = Enum.GetNames<OperationError>().Where(n => n != nameof(OperationError.None)).Select(s => $"Msg_Op_{s}").OrderBy(k => k, StringComparer.Ordinal);
        foreach (var culture in Cultures)
        {
            Assert.Equal(expected, Entries(culture).Keys.Where(k => k.StartsWith("Msg_Op_", StringComparison.Ordinal)).OrderBy(k => k, StringComparer.Ordinal));
        }
    }

    [Fact]
    public void Language_files_have_no_duplicate_keys()
    {
        foreach (var culture in Cultures)
        {
            var path = Path.Combine(RepoPaths.Languages, $"Strings.{culture}.xaml");
            var keys = XDocument.Load(path).Root!.Elements().Select(e => (string?)e.Attribute(X + "Key")).Where(k => k is not null).ToList();
            var duplicates = keys.GroupBy(k => k, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.True(duplicates.Count == 0, $"{culture} duplicates: {string.Join(", ", duplicates)}");
        }
    }
}
