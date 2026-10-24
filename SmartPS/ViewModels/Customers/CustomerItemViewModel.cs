using System;
using SmartPS.Models.Parking;
using SmartPS.Services.Localization;

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
    public string StatusText => HasActiveTicket ? AppText.Get("Str_Customers_ItemTicketActive") : (string.IsNullOrEmpty(MonthlyTicketCode) ? AppText.Get("Str_Customers_ItemNoTicket") : AppText.Get("Str_Customers_ItemTicketInactive"));
    public string CustomerTypeDisplay => Type == CustomerType.Resident && !string.IsNullOrEmpty(ApartmentCode)
        ? AppText.Get("Str_Customers_ItemResidentApt", ApartmentCode) 
        : (Type == CustomerType.Resident ? AppText.Get("Str_Customers_ItemResident") : AppText.Get("Str_Customers_ItemExternal"));
}
