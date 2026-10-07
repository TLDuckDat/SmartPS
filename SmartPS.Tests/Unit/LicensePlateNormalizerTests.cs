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
    // ---- Fix round 1, G3 (CH1-02): non-ASCII letters/digits make a plate invalid instead of silently disappearing ------

    [Theory]
    [InlineData("１２３４５")]          // full-width digits
    [InlineData("５１Ｆ１２３４５")]
    [InlineData("RВ3A9F1C2")]          // Cyrillic VE
    [InlineData("51F-12З.45")]         // Cyrillic ZE
    [InlineData("51F12345é")]
    public void G3_non_ascii_letters_or_digits_are_invalid(string plate)
    {
        Assert.False(LicensePlateNormalizer.IsValid(plate));
    }

    [Theory]
    [InlineData("51F\u200B12345")]    // zero-width space is a separator
    [InlineData("51F\u00A0123\t45")]  // NBSP and tab are separators
    public void G3_invisible_separators_are_ignored(string plate)
    {
        Assert.True(LicensePlateNormalizer.IsValid(plate));
        Assert.Equal("51F12345", LicensePlateNormalizer.Normalize(plate));
    }

    // ---- Fix round 2, H1 (N2): any non-ASCII character that is not a separator makes the plate invalid -------------

    [Theory]
    [InlineData("51F12345\u2460")]        // ① circled digit one (No)
    [InlineData("51F\u2460\u2461345")]
    [InlineData("51F12345\u2160")]        // Ⅰ roman numeral one (Nl)
    [InlineData("51F12345\u00B9")]        // ¹ superscript one (No)
    [InlineData("51F12345\U0001D7CF")]    // 𝟏 mathematical bold digit one (surrogate pair, Nd)
    [InlineData("\u2460\u2461\u2462\u2463\u2464")]
    public void H1_unicode_numbers_and_surrogate_digits_are_invalid(string plate)
    {
        Assert.False(LicensePlateNormalizer.IsValid(plate));
    }

    [Theory]
    [InlineData("51F\u200B12345")]        // zero-width space (Cf)
    [InlineData("51F\u00A0123\t45")]      // NBSP (Zs) and tab (Cc)
    [InlineData("51F\u200D123\u2009" + "45")] // zero-width joiner (Cf) and thin space (Zs)
    [InlineData(" 51F-123.45 ")]
    public void H1_separators_remain_valid(string plate)
    {
        Assert.True(LicensePlateNormalizer.IsValid(plate));
        Assert.Equal("51F12345", LicensePlateNormalizer.Normalize(plate));
    }
}
