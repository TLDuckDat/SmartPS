using System.Text.Json;
using SmartPS.Models.Parking;
using SmartPS.Services.Localization;
using SmartPS.Services.Pricing;

namespace SmartPS.Services.GateControl;

/// <summary>
/// Triển khai mặc định của IParkingFeeCalculator: uỷ quyền cho <see cref="IPricingEngine"/> và dựng kết quả tương thích
/// (Duration, RawFee, TotalFee, IsMonthlyTicket, Message, FeeDetails) cho giao diện và cổng.
/// </summary>
public class StandardParkingFeeCalculator : IParkingFeeCalculator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IPricingEngine _engine;

    public StandardParkingFeeCalculator() : this(new PricingEngine())
    {
    }

    public StandardParkingFeeCalculator(IPricingEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public ParkingFeeCalculationResult CalculateFee(ParkingSession session, PricingRule? pricingRule, DateTime checkOutTime)
    {
        var policy = pricingRule is null ? PricingPolicy.Default(session.VehicleTypeId) : PricingPolicy.FromRule(pricingRule);
        // Phiên đã đánh dấu vé tháng mà không có hạn cụ thể: miễn phí toàn bộ lượt.
        var pass = session.IsMonthlyPass
            ? new MonthlyPassInfo(DateTime.MinValue, DateTime.MaxValue, MonthlyTicketStatus.Active)
            : null;
        return CalculateFee(session, policy, checkOutTime, pass, isOverQuota: false);
    }

    public ParkingFeeCalculationResult CalculateFee(ParkingSession session, PricingPolicy policy, DateTime checkOutTime,
        MonthlyPassInfo? monthlyPass, bool isOverQuota)
    {
        var result = _engine.Calculate(new PricingRequest
        {
            LicensePlate = session.LicensePlate,
            VehicleTypeId = session.VehicleTypeId,
            EntryUtc = session.CheckInTime,
            ExitUtc = checkOutTime,
            CustomerType = session.CustomerType,
            MonthlyPass = monthlyPass,
            IsOverQuota = isOverQuota,
            Policy = policy
        });

        var durationText = PricingEngine.FormatDuration(result.Duration);
        var details = result.IsMonthlyTicket
            ? AppText.Get("Str_Fee_MonthlyDetail", durationText)
            : AppText.Get("Str_Fee_Detail", durationText,
                string.Join("; ", result.Lines.Select(l => l.Description)), result.RawFee);

        return new ParkingFeeCalculationResult
        {
            Duration = result.Duration,
            RawFee = result.RawFee,
            TotalFee = result.Total,
            IsMonthlyTicket = result.IsMonthlyTicket,
            Message = AppText.Get(result.IsMonthlyTicket ? "Msg_Fee_MonthlyFree" : "Msg_Fee_Calculated"),
            FeeDetails = details,
            PolicyVersionId = result.PolicyVersionId,
            FeeBreakdownJson = JsonSerializer.Serialize(new
            {
                policyVersionId = result.PolicyVersionId,
                policyVersion = result.PolicyVersion,
                total = result.Total,
                isMonthlyTicket = result.IsMonthlyTicket,
                lines = result.Lines.Select(l => new { code = l.Code, description = l.Description, amount = l.Amount })
            }, JsonOptions)
        };
    }
}
