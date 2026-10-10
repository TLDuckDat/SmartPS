using System;
using SmartPS.Models.Parking;

namespace SmartPS.ViewModels.Customers;

public class CustomerItemViewModel : ViewModelBase
{
    public int CustomerId { get; set; }
    public int? VehicleId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? IdentityCard { get; set; }
    public string DefaultLicensePlate { get; set; } = string.Empty;
    public CustomerType Type { get; set; }
    public string VehicleTypeName { get; set; } = string.Empty;
    public string MonthlyTicketCode { get; set; } = string.Empty;
    public DateTime? TicketExpiry { get; set; }
    public string ApartmentCode { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool HasActiveTicket => TicketExpiry.HasValue && TicketExpiry.Value >= DateTime.UtcNow;
    public string StatusText => HasActiveTicket ? "Vé tháng đang hiệu lực" : (string.IsNullOrEmpty(MonthlyTicketCode) ? "Chưa đăng ký vé" : "Hết hạn / Đã khóa");
    public string CustomerTypeDisplay => Type == CustomerType.Resident && !string.IsNullOrEmpty(ApartmentCode)
        ? $"Cư Dân ({ApartmentCode})" 
        : (Type == CustomerType.Resident ? "Cư Dân" : "Khách bên ngoài");
}
