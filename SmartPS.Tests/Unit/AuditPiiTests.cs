using SmartPS.Services.Customers;

namespace SmartPS.Tests.Unit;

/// <summary>m7 / A19: the phone number in audit details keeps only its last 3 digits.</summary>
public class AuditPiiTests
{
    [Theory]
    [InlineData("0988123456", "*******456")]
    [InlineData("0912888999", "*******999")]
    [InlineData("1234", "*234")]
    [InlineData("123", "***")]
    [InlineData("12", "**")]
    [InlineData("1", "*")]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void MaskPhone(string? input, string expected)
    {
        Assert.Equal(expected, AuditPii.MaskPhone(input));
    }
}
