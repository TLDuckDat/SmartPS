using SmartPS.Models.Parking;

namespace SmartPS.ViewModels.Customers;

public class CustomerItemViewModel : ViewModelBase
{
    public int CustomerId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? IdentityCard { get; set; }
    public string DefaultLicensePlate { get; set; } = string.Empty;
    public CustomerType Type { get; set; }
    public string VehicleTypeName { get; set; } = string.Empty;
    public string MonthlyTicketCode { get; set; } = string.Empty;
    public DateTime? TicketExpiry { get; set; }
    public bool HasActiveTicket => TicketExpiry.HasValue && TicketExpiry.Value >= DateTime.UtcNow;
    public string StatusText => HasActiveTicket ? "Vé tháng đang hiệu lực" : "Chưa kích hoạt / Hết hạn";
    public string CustomerTypeDisplay => Type switch
    {
        CustomerType.VIP => "⭐ Khách VIP / Cư Dân",
        CustomerType.Loyal => "Khách Thân Quen",
        _ => "Khách Vãng Lai"
    };
}
