using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Input;

namespace SmartPS.Tests.Unit;

/// <summary>
/// T-M2b + addendum N4: services must use the block form <c>await using (var tx = await ..BeginAuditedTransactionAsync(..)) { }</c>
/// and must not wrap Begin in an async method. Also structural checks for R15 (audited writes) and R16 (read-only audit UI).
/// </summary>
public partial class ServicePatternTests
{
    [GeneratedRegex(@"await\s+using\s+var\s+\w+\s*=\s*await\s+[^;]*BeginAuditedTransactionAsync")]
    private static partial Regex DeclarationFormBegin();

    [GeneratedRegex(@"async\s+(System\.Threading\.Tasks\.)?Task<\s*AuditedTransaction\s*>")]
    private static partial Regex AsyncBeginWrapper();

    private static List<string> Matches(Regex regex)
        => RepoPaths.CSharpFiles(RepoPaths.Services)
            .SelectMany(f =>
            {
                var text = File.ReadAllText(f);
                return regex.Matches(text).Select(m => $"{Path.GetRelativePath(RepoPaths.Root, f)}: {m.Value}");
            })
            .ToList();

    [Fact]
    public void Services_never_use_declaration_form_for_audited_transactions()
    {
        var hits = Matches(DeclarationFormBegin());

        Assert.True(hits.Count == 0, "Declaration-form audited transactions found:" + Environment.NewLine + string.Join(Environment.NewLine, hits));
    }

    [Fact]
    public void No_async_method_returns_AuditedTransaction()
    {
        var hits = Matches(AsyncBeginWrapper());

        Assert.True(hits.Count == 0, "async Task<AuditedTransaction> found (breaks the AsyncLocal marker):" + Environment.NewLine + string.Join(Environment.NewLine, hits));
    }

    [Theory]
    [InlineData("Auth", "AuthService.cs")]
    [InlineData("Shifts", "ShiftService.cs")]
    [InlineData("GateControl", "GateControlService.cs")]
    [InlineData("Payment", "PaymentService.cs")]
    [InlineData("RolePermissions", "RolePermissionService.cs")]
    public void Business_write_services_use_audited_transactions(string folder, string file)
    {
        // R15: business writes and their audit row share one transaction.
        var source = File.ReadAllText(Path.Combine(RepoPaths.Services, folder, file));

        Assert.Contains("BeginAuditedTransactionAsync", source, StringComparison.Ordinal);
        Assert.Contains("AppendAsync", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Shifts", "ShiftService.cs")]
    [InlineData("GateControl", "GateControlService.cs")]
    [InlineData("Payment", "PaymentService.cs")]
    public void Audited_services_no_longer_open_plain_serializable_transactions(string folder, string file)
    {
        // Every former db.Database.BeginTransactionAsync(Serializable) in an audited path became an audited block (plan §4 C4).
        var source = File.ReadAllText(Path.Combine(RepoPaths.Services, folder, file));
        var plain = Regex.Matches(source, @"await\s+using\s+var\s+\w+\s*=\s*await\s+db\.Database\.BeginTransactionAsync\(\s*System\.Data\.IsolationLevel\.Serializable");

        if (file == "PaymentService.cs")
        {
            // CreatePaymentAsync's reservation transaction is intentionally not audited (plan §1.3).
            Assert.InRange(plain.Count, 0, 1);
        }
        else
        {
            Assert.Empty(plain);
        }
    }

    [Fact]
    public void AuditLogViewModel_exposes_no_edit_or_delete_commands()
    {
        // R16 / AC-14: the audit viewer is read-only (namespace per addendum N2).
        var type = typeof(SmartPS.App).Assembly.GetType("SmartPS.ViewModels.Audit.AuditLogViewModel");
        Assert.NotNull(type);

        var commandNames = type!.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => typeof(ICommand).IsAssignableFrom(p.PropertyType))
            .Select(p => p.Name)
            .ToList();

        Assert.Contains("VerifyCommand", commandNames);
        Assert.DoesNotContain(commandNames, n =>
            n.Contains("Edit", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("Delete", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("Remove", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("Update", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("Save", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Audit_query_service_has_only_a_query_method()
    {
        var methods = typeof(IAuditQueryService).GetMethods();

        Assert.Single(methods);
        Assert.Equal(nameof(IAuditQueryService.QueryAsync), methods[0].Name);
    }

    [Fact]
    public void New_screens_live_in_the_agreed_namespaces()
    {
        // Addendum N2 (Audit) and plan §4 C6 (RolePermissions).
        var assembly = typeof(SmartPS.App).Assembly;

        Assert.NotNull(assembly.GetType("SmartPS.ViewModels.Audit.AuditLogViewModel"));
        Assert.NotNull(assembly.GetType("SmartPS.Views.Audit.AuditLogView"));
        Assert.NotNull(assembly.GetType("SmartPS.ViewModels.RolePermissions.RolePermissionsViewModel"));
        Assert.NotNull(assembly.GetType("SmartPS.Views.RolePermissions.RolePermissionsView"));
    }
}
