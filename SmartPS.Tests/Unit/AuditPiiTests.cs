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
    // ---- Fix round 1, G8 (CH1-07, review F3): digit runs with separators are masked in free text --------------------

    [Theory]
    [InlineData("Nguyen Van A 0988 123 456", "0988 123 456")]
    [InlineData("SĐT 0988.123.456 gọi trước", "0988.123.456")]
    [InlineData("SĐT 0988-123-456", "0988-123-456")]
    [InlineData("CCCD 079123456789 nợ phí", "079123456789")]
    [InlineData("CMND 0791 2345 6789", "0791 2345 6789")]
    [InlineData("liên hệ 0905123456", "0905123456")]
    public void G8_MaskPhoneNumbersInText_masks_separated_digit_runs(string text, string secret)
    {
        var masked = AuditPii.MaskPhoneNumbersInText(text);

        Assert.DoesNotContain(secret, masked, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(?:\d[\s.\-]?){9,}", masked);
        Assert.Contains("*", masked, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Nợ phí 3 lần")]
    [InlineData("Căn A-1205, tầng 12")]
    [InlineData("Biển 51F-123.45")]   // 7 digits: not a phone
    [InlineData("")]
    public void G8_MaskPhoneNumbersInText_keeps_short_numbers(string text)
    {
        Assert.Equal(text, AuditPii.MaskPhoneNumbersInText(text));
    }

    [Fact]
    public void G8_MaskPhoneNumbersInText_keeps_the_surrounding_words()
    {
        var masked = AuditPii.MaskPhoneNumbersInText("Chủ xe Trần B, SĐT 0905 123 456, nợ phí");

        Assert.StartsWith("Chủ xe Trần B, SĐT ", masked, StringComparison.Ordinal);
        Assert.EndsWith(", nợ phí", masked, StringComparison.Ordinal);
    }

    // ---- Fix round 2, H2 (N7): '/', '_' and parentheses are separators inside digit runs too ------------------------

    [Theory]
    [InlineData("SĐT 098/812/3456 nhà riêng", "098/812/3456")]
    [InlineData("SĐT 0988_123_456", "0988_123_456")]
    [InlineData("gọi (0988) 123 456 sau 18h", "(0988) 123 456")]
    [InlineData("CCCD 079/123/456/789", "079/123/456/789")]
    public void H2_MaskPhoneNumbersInText_masks_digit_runs_with_slash_underscore_and_parentheses(string text, string secret)
    {
        var masked = AuditPii.MaskPhoneNumbersInText(text);

        Assert.DoesNotContain(secret, masked, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(?:\d[\s.\-/_()]?){9,}", masked);
        Assert.Contains("*", masked, StringComparison.Ordinal);
    }
}
