using SmartPS.Models.Parking;
using SmartPS.Services.Localization;

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
        if (days > 0) durationStr += AppText.Get("Str_Fee_Days", days);
        if (hours > 0) durationStr += AppText.Get("Str_Fee_Hours", hours);
        if (minutes > 0 || durationStr == "") durationStr += AppText.Get("Str_Fee_Minutes", minutes);

        // 1. Vé tháng
        if (session.IsMonthlyPass)
        {
            return new ParkingFeeCalculationResult
            {
                Duration = duration,
                RawFee = 0,
                TotalFee = 0,
                IsMonthlyTicket = true,
                Message = AppText.Get("Msg_Fee_MonthlyFree"),
                FeeDetails = AppText.Get("Str_Fee_MonthlyDetail", durationStr.Trim())
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
            feeDetailCalc += AppText.Get("Str_Fee_DaysPart", days, dailyPrice);
            
            double remainingHours = duration.TotalHours - (days * 24);
            if (remainingHours > 0)
            {
                if (remainingHours <= 4)
                {
                    fee += block4hPrice;
                    feeDetailCalc += AppText.Get("Str_Fee_ExtraBlock", remainingHours, block4hPrice);
                }
                else
                {
                    fee += dailyPrice;
                    feeDetailCalc += AppText.Get("Str_Fee_ExtraDay", remainingHours, dailyPrice);
                }
            }
        }
        else
        {
            if (duration.TotalHours <= 4)
            {
                fee = block4hPrice;
                feeDetailCalc = AppText.Get("Str_Fee_Block4h", block4hPrice);
            }
            else
            {
                fee = dailyPrice;
                feeDetailCalc = AppText.Get("Str_Fee_OneDay", dailyPrice);
            }
        }

        return new ParkingFeeCalculationResult
        {
            Duration = duration,
            RawFee = fee,
            TotalFee = Math.Max(0, Math.Round(fee, 0)),
            IsMonthlyTicket = false,
            Message = AppText.Get("Msg_Fee_Calculated"),
            FeeDetails = AppText.Get("Str_Fee_Detail", durationStr.Trim(), feeDetailCalc, fee)
        };
    }
}
