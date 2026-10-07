using System.Xml.Linq;
using SmartPS.Models.Parking;
using SmartPS.Services.Common;
using SmartPS.Services.Customers;

namespace SmartPS.Tests.Unit;

/// <summary>N1 / AC-15: every key added by the resident/visitor flow (C1, C6, C8, C9) exists and is non-empty in the 3 language files.</summary>
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

        // C1
        keys.AddRange(new[] { "Str_Perm_Customer_View", "Str_Perm_Customer_Manage", "Str_Perm_Blacklist_Manage", "Str_Perm_Module_Customer", "Str_Perm_Module_Blacklist" });

        // C6
        keys.AddRange(new[] { "Visitor", "Resident", "MonthlyPass", "Blacklisted", "CustomerLocked" }.Select(s => $"Str_Gate_Badge_{s}"));
        keys.AddRange(new[] { "Resident", "Visitor", "MonthlyPass" }.Select(s => $"Str_Gate_Group_{s}"));
        keys.AddRange(new[]
        {
            "BlacklistBlocked", "NoSlotFor", "SlotNotFound", "SlotVehicleTypeMismatch", "SlotAudienceNotAllowed", "SlotNotAvailable",
            "TicketExpiredDuringStay", "TicketNoLongerValid", "BlacklistExitWarning", "CheckInResident", "CheckInMonthly", "CheckInVisitor"
        }.Select(s => $"Msg_Gate_{s}"));

        // C8
        keys.AddRange(new[]
        {
            "Title", "Subtitle", "Tab_Customers", "Tab_Blacklist", "Kpi_Total", "Kpi_Residents", "Kpi_ActiveTickets", "Kpi_ExpiringSoon", "Kpi_Revenue",
            "SearchHint", "Filter_All", "Filter_Residents", "Filter_NonResidents", "Col_Name", "Col_Phone", "Col_Apartment", "Col_Plates", "Col_Type",
            "Col_TicketStatus", "Col_TicketEnd", "Col_Active", "New", "Save", "Cancel", "Lock", "Unlock", "Field_FullName", "Field_Phone", "Field_Email",
            "Field_IdentityCard", "Field_IsResident", "Field_Apartment", "Field_Building", "Field_Type", "Field_Notes", "Vehicles", "AddVehicle",
            "RemoveVehicle", "Field_Plate", "Field_VehicleType", "Tickets", "Field_Plan", "Field_StartDate", "CreateTicket", "RenewTicket",
            "SuspendTicket", "ResumeTicket", "SuspendReason", "ReadOnlyNotice", "PrevPage", "NextPage", "PageInfo", "Locked"
        }.Select(s => $"Str_Cust_{s}"));
        keys.AddRange(Enum.GetNames<CustomerType>().Select(s => $"Str_CustType_{s}"));
        keys.AddRange(Enum.GetNames<TicketDisplayStatus>().Select(s => $"Str_TicketStatus_{s}"));
        keys.AddRange(new[]
        {
            "Plate", "Reason", "CreatedAt", "CreatedBy", "Status", "RemovedAt", "RemovedBy", "RemoveReason", "Add", "Remove", "ShowHistory", "Active", "Removed"
        }.Select(s => $"Str_Blacklist_{s}"));
        keys.AddRange(new[]
        {
            "SaveSuccess", "VehicleAdded", "VehicleRemoved", "TicketCreated", "TicketRenewed", "TicketSuspended", "TicketResumed", "LoadError",
            "ConfirmRemoveVehicle", "ConfirmLock"
        }.Select(s => $"Msg_Cust_{s}"));
        keys.AddRange(new[] { "Added", "Removed" }.Select(s => $"Msg_Blacklist_{s}"));
        keys.AddRange(Enum.GetNames<OperationError>().Where(n => n != nameof(OperationError.None)).Select(s => $"Msg_Op_{s}"));
        keys.AddRange(Enum.GetNames<CustomerValidationError>().Select(s => $"Msg_CustomerValidation_{s}"));

        // C9
        keys.AddRange(Enum.GetNames<ZoneAudience>().Select(s => $"Str_Map_Audience_{s}"));
        keys.AddRange(new[] { "ZoneSummary", "SaveAudience", "Unzoned", "MaintenanceCount" }.Select(s => $"Str_Map_{s}"));
        keys.AddRange(new[] { "Available", "Occupied", "Maintenance" }.Select(s => $"Str_Map_Status_{s}"));
        keys.AddRange(new[] { "AudienceSaved", "AudienceSaveError" }.Select(s => $"Msg_Map_{s}"));

        return keys;
    }

    [Fact]
    public void Enum_driven_key_sets_have_the_expected_sizes()
    {
        Assert.Equal(18, Enum.GetNames<OperationError>().Count(n => n != nameof(OperationError.None)));
        Assert.Equal(12, Enum.GetNames<CustomerValidationError>().Length);
        Assert.Equal(6, Enum.GetNames<TicketDisplayStatus>().Length);
        Assert.Equal(3, Enum.GetNames<ZoneAudience>().Length);
        Assert.Equal(4, Enum.GetNames<CustomerType>().Length);
    }

    [Fact]
    public void Every_resident_visitor_key_exists_and_is_non_empty_in_all_languages()
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
