using SmartPS.Services.GateControl;

namespace SmartPS.Tests.Unit;

/// <summary>E1: plates typed or read by OCR with dashes, dots, spaces or lower case compare by their normalized form.</summary>
public class LicensePlateNormalizerTests
{
    [Theory]
    [InlineData("51F-123.45", "51F12345")]
    [InlineData(" 51f 123 45 ", "51F12345")]
    [InlineData("51F12345", "51F12345")]
    [InlineData("29b1-555.55", "29B155555")]
    [InlineData("30A_678/90", "30A67890")]
    public void Normalize_keeps_only_upper_case_letters_and_digits(string input, string expected)
    {
        Assert.Equal(expected, LicensePlateNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-.-")]
    public void Normalize_of_null_blank_or_separators_only_is_empty(string? input)
    {
        Assert.Equal(string.Empty, LicensePlateNormalizer.Normalize(input));
    }

    [Fact]
    public void Bounds_are_5_and_12()
    {
        Assert.Equal(5, LicensePlateNormalizer.MinLength);
        Assert.Equal(12, LicensePlateNormalizer.MaxLength);
    }

    [Theory]
    [InlineData("12345", true)]          // exactly 5
    [InlineData("1234", false)]          // 4
    [InlineData("ABCDEF123456", true)]   // exactly 12
    [InlineData("ABCDEF1234567", false)] // 13
    [InlineData("51F-123.45", true)]     // validity is checked on the normalized form
    [InlineData("1-2-3-4", false)]       // 4 after normalization
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValid_checks_the_normalized_length(string? input, bool expected)
    {
        Assert.Equal(expected, LicensePlateNormalizer.IsValid(input));
    }
}
