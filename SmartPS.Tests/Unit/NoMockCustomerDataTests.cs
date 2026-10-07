namespace SmartPS.Tests.Unit;

/// <summary>AC-15: the Customers screen no longer carries hard-coded customers; Pricing holds no customer data.</summary>
public class NoMockCustomerDataTests
{
    private static IEnumerable<string> CustomerViewModelFiles()
        => RepoPaths.CSharpFiles(Path.Combine(RepoPaths.ViewModels, "Customers"));

    [Theory]
    [InlineData("InitializeCustomers")]
    [InlineData("Nguyễn Văn Hùng")]
    [InlineData("MT-2026-00")]
    [InlineData("_allCustomers")]
    [InlineData("Khách VIP / Cư Dân")]
    public void Customers_view_models_contain_no_mock_data(string marker)
    {
        var files = CustomerViewModelFiles().ToList();
        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            Assert.False(File.ReadAllText(file).Contains(marker, StringComparison.Ordinal), $"{Path.GetFileName(file)} still contains '{marker}'");
        }
    }

    [Fact]
    public void Pricing_view_model_contains_no_customer_data()
    {
        var path = Path.Combine(RepoPaths.ViewModels, "Pricing", "PricingViewModel.cs");
        Assert.True(File.Exists(path), path);

        Assert.DoesNotContain("Customer", File.ReadAllText(path), StringComparison.Ordinal);
    }
}
