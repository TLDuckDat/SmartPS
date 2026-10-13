using System.Xml.Linq;

namespace SmartPS.Tests.Unit;

/// <summary>N1 / AC-18: the three language dictionaries have identical key sets and contain every key this task introduces.</summary>
public class LocalizationKeyParityTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly string[] Cultures = { "vi-VN", "en-US", "ja-JP" };

    private static List<(string Key, string Value)> Entries(string culture)
    {
        var path = Path.Combine(RepoPaths.Languages, $"Strings.{culture}.xaml");
        Assert.True(File.Exists(path), path);
        var doc = XDocument.Load(path);
        return doc.Root!.Elements()
            .Select(e => (Key: (string?)e.Attribute(X + "Key"), Value: e.Value))
            .Where(e => e.Key is not null)
            .Select(e => (e.Key!, e.Value))
            .ToList();
    }

    private static HashSet<string> Keys(string culture) => Entries(culture).Select(e => e.Key).ToHashSet(StringComparer.Ordinal);

    public static IEnumerable<string> RequiredNewKeys()
    {
        var keys = new List<string>
        {
            // C3
            "Msg_Auth_PermissionDenied", "Msg_User_LastAdminProtected", "Msg_User_CannotLockSelf", "Msg_User_CannotChangeOwnRole",
            "Msg_User_AdminRoleRequiresAdmin", "Msg_DeleteUser_HasHistory",
            // C5
            "Str_Menu_RolePermissions", "Str_Menu_AuditLog",
        };

        // C6
        keys.AddRange(new[] { "Title", "Subtitle", "Permission", "Save", "Reset", "AdminLocked", "ReadOnlyNotice" }.Select(s => $"Str_RolePerm_{s}"));
        keys.AddRange(new[] { "SaveSuccess", "NoChanges", "SaveError", "LoadError" }.Select(s => $"Msg_RolePerm_{s}"));
        keys.AddRange(Permissions.ModuleOrder.Select(m => $"Str_Perm_Module_{m}"));
        keys.AddRange(Permissions.GetAll().Select(p => $"Str_Perm_{p.Replace('.', '_')}"));

        // C7
        keys.AddRange(new[] { "Title", "Subtitle", "FromDate", "ToDate", "User", "Action", "Outcome", "Search", "SearchHint", "Apply", "Clear", "All" }
            .Select(s => $"Str_Audit_{s}"));
        keys.AddRange(new[] { "Id", "Time", "User", "Role", "Action", "Entity", "Outcome", "Machine" }.Select(s => $"Str_Audit_Col_{s}"));
        keys.AddRange(new[] { "Details", "PrevPage", "NextPage", "PageInfo", "Verify" }.Select(s => $"Str_Audit_{s}"));
        keys.AddRange(new[] { "Success", "Denied", "Failed" }.Select(s => $"Str_Audit_Outcome_{s}"));
        keys.AddRange(new[] { "VerifyOk", "VerifyBroken", "LoadError", "VerifyError" }.Select(s => $"Msg_Audit_{s}"));
        return keys;
    }

    [Fact]
    public void All_three_files_have_the_same_key_set()
    {
        var vi = Keys("vi-VN");
        var en = Keys("en-US");
        var ja = Keys("ja-JP");

        Assert.Empty(vi.Except(en));
        Assert.Empty(en.Except(vi));
        Assert.Empty(vi.Except(ja));
        Assert.Empty(ja.Except(vi));
    }

    [Theory]
    [InlineData("vi-VN")]
    [InlineData("en-US")]
    [InlineData("ja-JP")]
    public void No_duplicate_or_empty_keys(string culture)
    {
        var entries = Entries(culture);

        var duplicates = entries.GroupBy(e => e.Key, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(duplicates);
        Assert.All(entries, e => Assert.False(string.IsNullOrWhiteSpace(e.Value), $"{culture}:{e.Key} is empty"));
    }

    [Fact]
    public void Every_new_key_exists_in_every_language()
    {
        var required = RequiredNewKeys().ToList();
        foreach (var culture in Cultures)
        {
            var keys = Keys(culture);
            var missing = required.Where(k => !keys.Contains(k)).ToList();
            Assert.True(missing.Count == 0, $"{culture} is missing: {string.Join(", ", missing)}");
        }
    }

    [Fact]
    public void Existing_navigation_denied_key_is_kept()
    {
        foreach (var culture in Cultures)
        {
            Assert.Contains("Msg_Nav_AccessDenied", Keys(culture));
        }
    }

    [Fact]
    public void Permission_label_keys_cover_all_23_permissions()
    {
        var expected = Permissions.GetAll().Select(p => $"Str_Perm_{p.Replace('.', '_')}").ToList();

        Assert.Equal(23, expected.Count);
        Assert.Contains("Str_Perm_Audit_View", expected);
        Assert.Contains("Str_Perm_Payment_Refund", expected);
        foreach (var culture in Cultures)
        {
            var keys = Keys(culture);
            Assert.All(expected, k => Assert.Contains(k, keys));
        }
    }
}
