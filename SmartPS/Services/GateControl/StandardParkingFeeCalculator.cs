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

        int days = (int)Math.Floor(duration.TotalDays);
        int hours = duration.Hours;
        int minutes = duration.Minutes;
        
        string durationStr = "";
        if (days > 0) durationStr += $"{days} ngày ";
        if (hours > 0) durationStr += $"{hours} giờ ";
        if (minutes > 0 || durationStr == "") durationStr += $"{minutes} phút";

        // 1. Vé tháng
        if (session.IsMonthlyPass)
        {
            return new ParkingFeeCalculationResult
            {
                Duration = duration,
                RawFee = 0,
                TotalFee = 0,
                IsMonthlyTicket = true,
                Message = "Xe vé tháng được miễn cước lượt.",
                FeeDetails = $"Thời gian đỗ: {durationStr.Trim()}. Miễn cước phí (Vé tháng)."
            };
        }

        // 2. Tính phí (không còn ân hạn 15 phút)
        decimal block4hPrice = pricingRule?.Block4hPrice ?? (session.VehicleTypeId == 2 ? 25000 : 5000);
        decimal dailyPrice = pricingRule?.DailyPrice ?? (session.VehicleTypeId == 2 ? 100000 : 25000);

        decimal fee = 0;
        string feeDetailCalc = "";

        if (days > 0)
        {
            fee += days * dailyPrice;
            feeDetailCalc += $"{days} ngày ({dailyPrice:N0}đ/ngày)";
            
            double remainingHours = duration.TotalHours - (days * 24);
            if (remainingHours > 0)
            {
                if (remainingHours <= 4)
                {
                    fee += block4hPrice;
                    feeDetailCalc += $" + vượt {remainingHours:F1}h ({block4hPrice:N0}đ)";
                }
                else
                {
                    fee += dailyPrice;
                    feeDetailCalc += $" + vượt {remainingHours:F1}h ({dailyPrice:N0}đ)";
                }
            }
        }
        else
        {
            if (duration.TotalHours <= 4)
            {
                fee = block4hPrice;
                feeDetailCalc = $"Khung 4h ({block4hPrice:N0}đ)";
            }
            else
            {
                fee = dailyPrice;
                feeDetailCalc = $"Gói 1 ngày ({dailyPrice:N0}đ)";
            }
        }

        return new ParkingFeeCalculationResult
        {
            Duration = duration,
            RawFee = fee,
            TotalFee = Math.Max(0, Math.Round(fee, 0)),
            IsMonthlyTicket = false,
            Message = "Tính cước thành công.",
            FeeDetails = $"Thời gian đỗ: {durationStr.Trim()}. Cước phí: {feeDetailCalc} = {fee:N0}đ."
        };
    }
}
