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
    [InlineData("51F\u00A0123 45")]  // NBSP and space are visible separators
    [InlineData("51F\u2009123-45")]  // thin space
    public void G3_visible_space_separators_are_ignored(string plate)
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
    [InlineData("51F\u00A0123\u2009" + "45")] // NBSP (Zs) and thin space (Zs)
    [InlineData(" 51F-123.45 ")]
    public void H1_separators_remain_valid(string plate)
    {
        Assert.True(LicensePlateNormalizer.IsValid(plate));
        Assert.Equal("51F12345", LicensePlateNormalizer.Normalize(plate));
    }

    // ---- Branch 2 fix round 1, H4: control / format characters and raw length over the 20-char column -------------

    [Theory]
    [InlineData("51F\u200B12345")]   // zero-width space (Cf)
    [InlineData("51F\u200D12345")]   // zero-width joiner (Cf)
    [InlineData("51F\uFEFF12345")]   // zero-width no-break space / BOM (Cf)
    [InlineData("51F\u202E12345")]   // right-to-left override (Cf)
    [InlineData("51F123\t45")]       // tab (Cc)
    [InlineData("51F123\n45")]       // line feed (Cc)
    [InlineData("51F123\u000045")]   // NUL (Cc)
    [InlineData("51F123\u007F45")]   // DEL (Cc)
    [InlineData("51F123\u008545")]   // NEL (Cc)
    public void H4_control_and_format_characters_make_the_plate_invalid(string plate)
    {
        Assert.False(LicensePlateNormalizer.IsValid(plate));
        Assert.Equal("51F12345", LicensePlateNormalizer.Normalize(plate)); // Normalize itself is unchanged
    }

    [Theory]
    [InlineData(20, true)]
    [InlineData(21, false)]
    [InlineData(40, false)]
    public void H4_trimmed_raw_plate_longer_than_20_characters_is_invalid(int rawLength, bool expected)
    {
        // "51F" + dashes + "12345": the normalized form stays 8 characters, only the raw length changes.
        var plate = "51F" + new string('-', rawLength - 8) + "12345";
        Assert.Equal(rawLength, plate.Length);

        Assert.Equal(expected, LicensePlateNormalizer.IsValid(plate));
    }

    [Fact]
    public void H4_surrounding_whitespace_does_not_count_towards_the_raw_length()
    {
        var plate = new string(' ', 15) + "51F-123.45" + new string(' ', 15);

        Assert.True(LicensePlateNormalizer.IsValid(plate));
    }
}
