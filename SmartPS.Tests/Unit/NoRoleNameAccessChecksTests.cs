using System.Text.RegularExpressions;

namespace SmartPS.Tests.Unit;

/// <summary>
/// AC-17 / R1: no role-name string literal is used for access decisions in ViewModels or Services.
/// Allow-list (plan §0.7, §1.1): SystemRoles.cs (the single system-admin literal) and
/// UserManagementViewModel.cs (KPI counters / filter display only).
/// </summary>
public partial class NoRoleNameAccessChecksTests
{
    private static readonly string[] AllowList = { "SystemRoles.cs", "UserManagementViewModel.cs" };

    [GeneratedRegex("\"(Admin|Manager|Operator)\"")]
    private static partial Regex RoleLiteral();

    [GeneratedRegex(@"\b(AllowedRoles|IsManagerRole)\b")]
    private static partial Regex LegacyRoleGate();

    // Comparing against SystemRoles.Admin (system-admin identification, e.g. in an EF query) is the AC-17 exception.
    [GeneratedRegex(@"RoleName\s*(==|!=|\.Equals\s*\()\s*(?!SystemRoles\.)")]
    private static partial Regex RoleNameComparison();

    private static IEnumerable<(string File, int Line, string Text)> ScanCode(Regex pattern)
    {
        var files = RepoPaths.CSharpFiles(RepoPaths.ViewModels).Concat(RepoPaths.CSharpFiles(RepoPaths.Services));
        foreach (var file in files)
        {
            if (AllowList.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var code = StripLineComment(lines[i]);
                if (pattern.IsMatch(code))
                {
                    yield return (Path.GetRelativePath(RepoPaths.Root, file), i + 1, lines[i].Trim());
                }
            }
        }
    }

    private static string StripLineComment(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("*", StringComparison.Ordinal) || trimmed.StartsWith("/*", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var idx = line.IndexOf("//", StringComparison.Ordinal);
        return idx >= 0 && line.LastIndexOf('"', idx) < 0 ? line[..idx] : line;
    }

    private static string Describe(IEnumerable<(string File, int Line, string Text)> hits)
        => string.Join(Environment.NewLine, hits.Select(h => $"{h.File}:{h.Line}: {h.Text}"));

    [Fact]
    public void Folders_to_scan_exist()
    {
        Assert.True(Directory.Exists(RepoPaths.ViewModels), RepoPaths.ViewModels);
        Assert.True(Directory.Exists(RepoPaths.Services), RepoPaths.Services);
        Assert.True(File.Exists(Path.Combine(RepoPaths.Services, "Authorization", "SystemRoles.cs")), "SystemRoles.cs is the allow-listed home of the Admin literal");
    }

    [Fact]
    public void No_role_name_literals_outside_allow_list()
    {
        var hits = ScanCode(RoleLiteral()).ToList();

        Assert.True(hits.Count == 0, "Role-name literals used outside the allow-list:" + Environment.NewLine + Describe(hits));
    }

    [Fact]
    public void Legacy_role_gates_are_removed()
    {
        var hits = ScanCode(LegacyRoleGate()).ToList();

        Assert.True(hits.Count == 0, "AllowedRoles / IsManagerRole still referenced:" + Environment.NewLine + Describe(hits));
    }

    [Fact]
    public void No_RoleName_comparisons_outside_allow_list()
    {
        var hits = ScanCode(RoleNameComparison()).ToList();

        Assert.True(hits.Count == 0, "RoleName comparisons outside the allow-list:" + Environment.NewLine + Describe(hits));
    }

    [Fact]
    public void Dashboard_has_no_admin_fallback()
    {
        // R4: the '?? "Admin"' fallback in DashboardViewModel is gone.
        var source = File.ReadAllText(Path.Combine(RepoPaths.ViewModels, "Dashboard", "DashboardViewModel.cs"));

        Assert.DoesNotContain("?? \"Admin\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Str_Dash_DefaultAdminName", source, StringComparison.Ordinal);
    }
}
