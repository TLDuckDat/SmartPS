using SmartPS.Models.Parking;

namespace SmartPS.Services.GateControl;

/// <summary>
/// Triển khai mặc định của IParkingFeeCalculator hỗ trợ vé tháng, biểu phí block + lũy tiến + phụ phí đêm + chiết khấu hạng khách hàng.
/// </summary>
public class StandardParkingFeeCalculator : IParkingFeeCalculator
{
    public ParkingFeeCalculationResult CalculateFee(ParkingSession session, PricingRule? pricingRule, DateTime checkOutTime)
    {
        var duration = checkOutTime - session.CheckInTime;
        if (duration.TotalSeconds < 0) duration = TimeSpan.FromSeconds(1);

        // Trường hợp 1: Xe vé tháng -> Miễn cước lượt
        if (session.IsMonthlyPass)
        {
            return new ParkingFeeCalculationResult
            {
                Duration = duration,
                RawFee = 0,
                DiscountPercentage = 100,
                TotalFee = 0,
                IsMonthlyTicket = true,
                Message = "Xe vé tháng được miễn cước phí."
            };
        }

        // Trường hợp 2: Tính cước theo biểu phí
        decimal fee = 0;
        if (pricingRule != null)
        {
            var firstBlockMin = pricingRule.FirstBlockMinutes > 0 ? pricingRule.FirstBlockMinutes : 120;
            if (duration.TotalMinutes <= firstBlockMin)
            {
                fee = pricingRule.FirstBlockPrice;
            }
            else
            {
                var extraMinutes = duration.TotalMinutes - firstBlockMin;
                var extraHours = (decimal)Math.Ceiling(extraMinutes / 60.0);
                fee = pricingRule.FirstBlockPrice + (extraHours * pricingRule.AdditionalPricePerHour);
            }

            // Phụ phí qua đêm nếu gửi trên 12 tiếng hoặc gửi qua đêm (22h đến 6h)
            if (duration.TotalHours >= 12 || (session.CheckInTime.Hour >= 22 && checkOutTime.Hour <= 6))
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
        double discount = GetCustomerDiscount(session.Customer);
        var totalFee = fee * (decimal)(1 - discount / 100.0);

        return new ParkingFeeCalculationResult
        {
            Duration = duration,
            RawFee = fee,
            DiscountPercentage = discount,
            TotalFee = Math.Max(0, Math.Round(totalFee, 0)),
            IsMonthlyTicket = false,
            Message = "Tính cước thành công."
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
