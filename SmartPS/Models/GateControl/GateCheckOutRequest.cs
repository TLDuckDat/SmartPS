using SmartPS.Models.Parking;

namespace SmartPS.Models.GateControl;

public class GateCheckOutRequest
{
    public int SessionId { get; set; }
    public string? CheckOutImagePath { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public decimal TotalFee { get; set; }
}
