using SmartPS.Services.Common;

namespace SmartPS.Services.Pricing;

/// <summary>Bảng giá hiện hành của một loại xe để hiển thị và chỉnh trên màn Bảng giá.</summary>
public sealed record PricingRuleData
{
    public int RuleId { get; init; }
    public int VehicleTypeId { get; init; }
    public string VehicleTypeName { get; init; } = string.Empty;
    public decimal Block4hPrice { get; init; }
    public decimal DailyPrice { get; init; }
    public decimal Monthly1Price { get; init; }
    public decimal Monthly3Price { get; init; }
    public decimal Monthly6Price { get; init; }
    public int GracePeriodMinutes { get; init; }
    public decimal OvernightSurcharge { get; init; }
    public decimal OverQuotaDailyPrice { get; init; }
    public string? Description { get; init; }

    /// <summary>
    /// Dấu hiệu giá trị hiện hành của loại xe lúc tải (kiểm tra đồng thời lạc quan). Khi lưu: giá trị người dùng gửi
    /// trùng dấu hiệu này nghĩa là không đổi (bỏ qua); khác dấu hiệu mà CSDL đã khác dấu hiệu là xung đột. Null thì so với CSDL.
    /// </summary>
    public string? LoadedStamp { get; init; }

    /// <summary>Dấu hiệu theo các giá trị cấu hình của bản ghi này.</summary>
    public string ValuesStamp() => string.Join('|', Block4hPrice, DailyPrice, Monthly1Price, Monthly3Price, Monthly6Price,
        GracePeriodMinutes, OvernightSurcharge, OverQuotaDailyPrice);
}

/// <summary>Toàn bộ cấu hình trên màn Bảng giá: bảng giá từng loại xe và số xe tối đa mỗi hộ.</summary>
/// <param name="LoadedDefaultMaxVehicles">Số xe tối đa lúc tải, dùng cho kiểm tra đồng thời; null thì so với CSDL.</param>
public sealed record PricingConfiguration(IReadOnlyList<PricingRuleData> Rules, int DefaultMaxVehiclesPerHousehold, int? LoadedDefaultMaxVehicles = null);

public interface IPricingService
{
    /// <summary>Số phút ân hạn tối đa được phép cấu hình.</summary>
    public const int MaxGracePeriodMinutes = 120;

    /// <summary>Đọc bảng giá hiện hành. Cần quyền Pricing.Manage; thiếu quyền thì ghi ACCESS_DENIED và ném PermissionDeniedException.</summary>
    Task<PricingConfiguration> LoadPoliciesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lưu bảng giá (quyền Pricing.Manage). Kiểm tra dữ liệu ở service; mỗi loại xe có thay đổi tạo thêm một
    /// PricingPolicyVersions và ghi PRICING_UPDATED (giá trị cũ/mới) trong cùng giao dịch với việc lưu.
    /// Không có gì thay đổi thì không tạo phiên bản và không ghi nhật ký. Kiểm tra đồng thời lạc quan: loại xe người dùng
    /// không đổi so với lúc tải thì bỏ qua (không ghi đè thay đổi của người khác); loại xe (hoặc số xe tối đa) người dùng đổi
    /// mà CSDL đã bị người khác đổi từ lúc tải thì trả OperationError.Validation kèm thông báo xung đột (yêu cầu tải lại) và KHÔNG ghi gì (tất cả hoặc không gì).
    /// </summary>
    Task<OperationResult> SavePoliciesAsync(PricingConfiguration configuration, CancellationToken cancellationToken = default);
}
