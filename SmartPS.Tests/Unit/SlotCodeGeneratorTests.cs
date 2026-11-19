using SmartPS.Services.ParkingZones;

namespace SmartPS.Tests.Unit;

/// <summary>Sinh mã ô đỗ hàng loạt: nối tiếp số lớn nhất, giữ độ rộng chữ số, gợi ý tiền tố theo khu.</summary>
public class SlotCodeGeneratorTests
{
    [Fact]
    public void Next_codes_continue_after_the_largest_number_of_the_prefix()
    {
        var existing = new[] { "C-01", "C-07", "C-30", "A-99", "CX-50" };

        Assert.Equal(new[] { "C-31", "C-32", "C-33" }, SlotCodeGenerator.NextCodes("C-", existing, 3));
        Assert.Equal(new[] { "D-01", "D-02" }, SlotCodeGenerator.NextCodes("D-", existing, 2));
    }

    [Fact]
    public void Next_codes_keep_the_existing_digit_width_and_grow_past_99()
    {
        Assert.Equal(new[] { "R-M005" }, SlotCodeGenerator.NextCodes("R-M", new[] { "R-M004" }, 1));
        Assert.Equal(new[] { "A-99", "A-100" }, SlotCodeGenerator.NextCodes("A-", new[] { "A-98" }, 2));
    }

    [Fact]
    public void Codes_with_other_suffixes_are_ignored()
    {
        // "R-M01" thuộc tiền tố "R-M", không phải "R-"; "C-A1" không phải số
        Assert.Equal(new[] { "R-01" }, SlotCodeGenerator.NextCodes("R-", new[] { "R-M01", "R-C02" }, 1));
        Assert.Equal(new[] { "C-01" }, SlotCodeGenerator.NextCodes("C-", new[] { "C-A1" }, 1));
    }

    [Theory]
    [InlineData("ZONE_C", new string[0], "C-")]
    [InlineData("ZONE_R", new[] { "R-M01", "R-C01" }, "R-M")]
    [InlineData("KHU_MOI", new string[0], "KHUMOI-")]
    [InlineData("", new string[0], "S-")]
    public void Suggested_prefix_comes_from_existing_codes_or_the_zone_code(string zoneCode, string[] existing, string expected)
    {
        Assert.Equal(expected, SlotCodeGenerator.SuggestPrefix(zoneCode, existing));
    }

    [Theory]
    [InlineData("C-", true)]
    [InlineData("R-M", true)]
    [InlineData("B2-", true)]
    [InlineData("A1", false)]      // kết thúc bằng chữ số: mã sinh ra sẽ mơ hồ
    [InlineData("", false)]
    [InlineData("Ô-", false)]      // chữ có dấu
    [InlineData("ABCDEFGHIJK", false)] // quá 10 ký tự
    public void Prefix_validation(string prefix, bool valid)
    {
        Assert.Equal(valid, SlotCodeGenerator.IsValidPrefix(SlotCodeGenerator.NormalizePrefix(prefix)));
    }

    [Fact]
    public void Normalize_prefix_trims_and_uppercases()
    {
        Assert.Equal("C-", SlotCodeGenerator.NormalizePrefix(" c - "));
    }
}
