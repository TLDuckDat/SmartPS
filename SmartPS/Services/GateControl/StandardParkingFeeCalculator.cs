using SmartPS.Models.Parking;

namespace SmartPS.Services.GateControl;

/// <summary>
/// Triển khai mặc định của IParkingFeeCalculator hỗ trợ vé tháng, biểu phí block + lũy tiến + phụ phí đêm + chiết khấu hạng khách hàng.
/// </summary>
public class StandardParkingFeeCalculator : IParkingFeeCalculator
{
    public ParkingFeeCalculationResult CalculateFee(ParkingSession session, PricingRule? pricingRule, DateTime checkOutTime)
        => CalculateFee(session, pricingRule, checkOutTime, null);

    public ParkingFeeCalculationResult CalculateFee(ParkingSession session, PricingRule? pricingRule, DateTime checkOutTime, MonthlyCoverage? coverage)
    {
        var duration = checkOutTime - session.CheckInTime;
        if (duration.TotalSeconds < 0) duration = TimeSpan.FromSeconds(1);

        var window = MonthlyFeeSplitPolicy.Resolve(session.CheckInTime, checkOutTime, session.IsMonthlyPass, coverage);

        // Trường hợp 1: Xe vé tháng còn hiệu lực (hoặc chưa tra cứu được) -> Miễn cước lượt
        if (window.IsFree)
        {
            return new ParkingFeeCalculationResult
            {
                Duration = duration,
                RawFee = 0,
                DiscountPercentage = 100,
                TotalFee = 0,
                IsMonthlyTicket = true,
                Message = "Xe vé tháng được miễn cước phí.",
                ChargeReason = window.Reason,
                ChargeFromUtc = null,
                TicketValidUntilUtc = window.TicketValidUntilUtc
            };
        }

        // Trường hợp 2: Tính cước theo biểu phí. Vé tháng hết hạn chỉ bị tính phần thời gian không được bao phủ,
        // theo giá vãng lai và không áp chiết khấu hạng khách hàng.
        var isMonthlyCharge = window.Reason != MonthlyChargeReason.NotMonthly;
        var chargeSession = isMonthlyCharge
            ? new ParkingSession
            {
                SessionId = session.SessionId,
                LicensePlate = session.LicensePlate,
                VehicleTypeId = session.VehicleTypeId,
                CheckInTime = window.ChargeFromUtc ?? session.CheckInTime,
                IsMonthlyPass = false,
                Customer = null
            }
            : session;

        var chargeDuration = checkOutTime - chargeSession.CheckInTime;
        if (chargeDuration.TotalSeconds < 0) chargeDuration = TimeSpan.FromSeconds(1);

        decimal fee = 0;
        if (pricingRule != null)
        {
            var firstBlockMin = pricingRule.FirstBlockMinutes > 0 ? pricingRule.FirstBlockMinutes : 120;
            if (chargeDuration.TotalMinutes <= firstBlockMin)
            {
                fee = pricingRule.FirstBlockPrice;
            }
            else
            {
                var extraMinutes = chargeDuration.TotalMinutes - firstBlockMin;
                var extraHours = (decimal)Math.Ceiling(extraMinutes / 60.0);
                fee = pricingRule.FirstBlockPrice + (extraHours * pricingRule.AdditionalPricePerHour);
            }

            // Phụ phí qua đêm nếu gửi trên 12 tiếng hoặc gửi qua đêm (22h đến 6h)
            if (chargeDuration.TotalHours >= 12 || (chargeSession.CheckInTime.Hour >= 22 && checkOutTime.Hour <= 6))
            {
                fee += pricingRule.OvernightPrice;
            }
        }
        else
        {
            // Mặc định an toàn: xe máy 5,000đ, ô tô 25,000đ
            fee = session.VehicleTypeId == 2 ? 25000 : 5000;
        }

        // Chiết khấu theo hạng khách hàng (CustomerTier / CustomerType)
        double discount = GetCustomerDiscount(chargeSession.Customer);
        var totalFee = fee * (decimal)(1 - discount / 100.0);

        return new ParkingFeeCalculationResult
        {
            Duration = duration,
            RawFee = fee,
            DiscountPercentage = discount,
            TotalFee = Math.Max(0, Math.Round(totalFee, 0)),
            IsMonthlyTicket = false,
            Message = window.Reason switch
            {
                MonthlyChargeReason.ExpiredDuringStay => "Vé tháng hết hạn trong lúc gửi, tính cước vãng lai cho phần sau khi hết hạn.",
                MonthlyChargeReason.NotCovered => "Vé tháng không còn hiệu lực, tính cước vãng lai.",
                _ => "Tính cước thành công."
            },
            ChargeReason = window.Reason,
            ChargeFromUtc = isMonthlyCharge ? window.ChargeFromUtc : null,
            TicketValidUntilUtc = window.TicketValidUntilUtc
        };
    }

    private static double GetCustomerDiscount(Customer? customer)
    {
        if (customer == null) return 0;

        return customer.Type switch
        {
            CustomerType.Loyal => 10,
            CustomerType.VIP => 20,
            _ => 0
        };
    }
}
