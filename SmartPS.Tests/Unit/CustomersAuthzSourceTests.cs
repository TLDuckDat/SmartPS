using System.Text.RegularExpressions;

namespace SmartPS.Tests.Unit;

/// <summary>KAN-33b: the customers / tickets / vehicles module uses IAuditService and IAuthorizationGuard, not the gate JSONL log.</summary>
public class CustomersAuthzSourceTests
{
    private static string CustomersServices => Path.Combine(RepoPaths.Services, "Customers");

    private static string CustomersViewModels => Path.Combine(RepoPaths.ViewModels, "Customers");

    private static IEnumerable<string> ModuleFiles() =>
        RepoPaths.CSharpFiles(CustomersServices).Concat(RepoPaths.CSharpFiles(CustomersViewModels));

    [Fact]
    public void Module_does_not_reference_the_gate_jsonl_log_or_write_files()
    {
        var files = ModuleFiles().ToList();
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("gate_audit_log", text, StringComparison.Ordinal);
            Assert.DoesNotContain(".jsonl", text, StringComparison.OrdinalIgnoreCase);
            Assert.False(Regex.IsMatch(text, @"File\.(Append|Write)"), $"{Path.GetFileName(file)} writes files directly");
            Assert.DoesNotContain("LogAuditLineAsync", text, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("CustomersService.cs")]
    [InlineData("TicketsService.cs")]
    [InlineData("VehiclesService.cs")]
    public void Services_demand_ParkingConfigure_and_use_audited_transactions(string fileName)
    {
        var text = File.ReadAllText(Path.Combine(CustomersServices, fileName));

        Assert.Contains("IAuthorizationGuard", text, StringComparison.Ordinal);
        Assert.Contains("Permissions.ParkingConfigure", text, StringComparison.Ordinal);
        Assert.Contains("IAuditService", text, StringComparison.Ordinal);
        Assert.Contains("BeginAuditedTransactionAsync", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Database.BeginTransactionAsync", text, StringComparison.Ordinal);
    }
}
