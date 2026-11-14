using SmartPS.Models.Parking;

namespace SmartPS.Services.Pricing;

/// <summary>
/// Bảng giá của một loại xe tại một thời điểm (bản chụp). Engine chỉ đọc bản chụp này, không đọc CSDL.
/// </summary>
public sealed record PricingPolicy
{
    public const int DefaultGraceMinutes = 15;
    public const int DefaultBlockHours = 4;

    public int VehicleTypeId { get; init; }
    public decimal Block4hPrice { get; init; }
    public decimal DailyPrice { get; init; }
    public int GracePeriodMinutes { get; init; } = DefaultGraceMinutes;

    /// <summary>Phụ phí cố định cho mỗi lần khoảng gửi đi qua mốc 00:00 giờ Việt Nam.</summary>
    public decimal OvernightSurcharge { get; init; }

    /// <summary>Giá mỗi chu kỳ 24h của xe vượt định mức (không ân hạn, không tính theo khung).</summary>
    public decimal OverQuotaDailyPrice { get; init; }

    public int BlockHours { get; init; } = DefaultBlockHours;

    /// <summary>Mã bản chụp trong PricingPolicyVersions; null khi bảng giá không đến từ CSDL (dự phòng, mặc định).</summary>
    public int? PolicyVersionId { get; init; }

    /// <summary>Số thứ tự phiên bản của loại xe (1, 2, ...); null khi không có bản chụp.</summary>
    public int? PolicyVersion { get; init; }

    /// <summary>
    /// Bảng giá mặc định (khi chưa có bảng giá trong CSDL), khớp dữ liệu mẫu seed_data.sql:
    /// 2 = ô tô (25.000 / 100.000), 3 = xe đạp/xe điện (2.000 / 10.000), còn lại xe máy (5.000 / 25.000).
    /// Giá vượt định mức mặc định bằng giá ngày; phụ phí qua đêm mặc định 0.
    /// </summary>
    public static PricingPolicy Default(int vehicleTypeId)
    {
        var (block, daily) = vehicleTypeId switch
        {
            2 => (25000m, 100000m),
            3 => (2000m, 10000m),
            _ => (5000m, 25000m)
        };
        return new PricingPolicy
        {
            VehicleTypeId = vehicleTypeId,
            Block4hPrice = block,
            DailyPrice = daily,
            OverQuotaDailyPrice = daily
        };
    }

    /// <summary>Chuyển bản ghi PricingRules hiện hành thành bảng giá (chưa gắn phiên bản).</summary>
    public static PricingPolicy FromRule(PricingRule rule) => new()
    {
        VehicleTypeId = rule.VehicleTypeId,
        Block4hPrice = rule.Block4hPrice,
        DailyPrice = rule.DailyPrice,
        GracePeriodMinutes = rule.GracePeriodMinutes,
        OvernightSurcharge = rule.OvernightSurcharge,
        OverQuotaDailyPrice = rule.OverQuotaDailyPrice
    };
}

/// <summary>Vé tháng của xe, mốc thời gian là UTC (cùng quy ước với bảng MonthlyTickets).</summary>
public sealed record MonthlyPassInfo(DateTime? StartDateUtc, DateTime? EndDateUtc, MonthlyTicketStatus? Status);

public sealed record PricingRequest
{
    public string? LicensePlate { get; init; }
    public int? VehicleId { get; init; }
    public int VehicleTypeId { get; init; }
    public DateTime EntryUtc { get; init; }
    public DateTime ExitUtc { get; init; }
    public CustomerType CustomerType { get; init; }
    public MonthlyPassInfo? MonthlyPass { get; init; }
    public bool IsOverQuota { get; init; }
    public required PricingPolicy Policy { get; init; }
}

/// <summary>Một dòng của bảng chi tiết cước. <see cref="Description"/> đã được dịch theo ngôn ngữ lúc tính.</summary>
public sealed record FeeLine(string Code, string Description, decimal Amount);

public sealed record PricingResult
{
    public decimal Total { get; init; }

    /// <summary>Tổng trước khi làm tròn.</summary>
    public decimal RawFee { get; init; }

    public IReadOnlyList<FeeLine> Lines { get; init; } = Array.Empty<FeeLine>();
    public int? PolicyVersionId { get; init; }
    public int? PolicyVersion { get; init; }

    /// <summary>True khi toàn bộ lượt gửi nằm trong hạn vé tháng.</summary>
    public bool IsMonthlyTicket { get; init; }

    public TimeSpan Duration { get; init; }
}

public static class FeeLineCodes
{
    public const string Grace = "GRACE";
    public const string MonthlyFree = "MONTHLY_FREE";
    public const string Cycle = "CYCLE";
    public const string OverQuota = "OVER_QUOTA";
    public const string Overnight = "OVERNIGHT";
}

public interface IPricingEngine
{
    /// <summary>Tính cước bằng hàm thuần: cùng đầu vào luôn cho cùng kết quả, không đọc CSDL hay đồng hồ.</summary>
    PricingResult Calculate(PricingRequest request);
}
