using SmartPS.Services.Customers;
using SmartPS.Services.Localization;

namespace SmartPS.ViewModels.Customers;

/// <summary>Một dòng khách hàng trong danh sách, bao bọc <see cref="CustomerListItem"/> và dịch các nhãn theo ngôn ngữ hiện tại.</summary>
public class CustomerItemViewModel : ViewModelBase
{
    private readonly ILocalizationService _localization;

    public CustomerItemViewModel(CustomerListItem item, ILocalizationService localization)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
    }

    public CustomerListItem Item { get; }

    public int CustomerId => Item.CustomerId;
    public string FullName => Item.FullName;
    public string PhoneNumber => Item.PhoneNumber;
    public bool IsResident => Item.IsResident;
    public string ApartmentCode => Item.ApartmentCode ?? string.Empty;
    public bool IsActive => Item.IsActive;
    public string LicensePlatesText => string.Join(", ", Item.LicensePlates);
    public string PrimaryTicketCode => Item.PrimaryTicketCode ?? string.Empty;

    /// <summary>Ngày cuối cùng vé còn hiệu lực (giờ Việt Nam), rỗng khi khách chưa có vé.</summary>
    public string TicketEndText => Item.PrimaryTicketEndUtc.HasValue
        ? TicketDates.LastValidDateVn(Item.PrimaryTicketEndUtc.Value).ToString("dd/MM/yyyy")
        : string.Empty;

    public string TypeText => _localization.GetString($"Str_CustType_{Item.Type}");
    public string TicketStatusText => _localization.GetString($"Str_TicketStatus_{Item.TicketStatus}");
    public string LockedText => Item.IsActive ? string.Empty : _localization.GetString("Str_Cust_Locked");

    public bool HasActiveTicket => Item.TicketStatus is TicketDisplayStatus.Active or TicketDisplayStatus.ExpiringSoon;
    public bool IsExpiringSoon => Item.TicketStatus == TicketDisplayStatus.ExpiringSoon;
    public bool IsExpired => Item.TicketStatus == TicketDisplayStatus.Expired;

    /// <summary>Gọi khi đổi ngôn ngữ để các nhãn dịch được cập nhật.</summary>
    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(TypeText));
        OnPropertyChanged(nameof(TicketStatusText));
        OnPropertyChanged(nameof(LockedText));
    }
}
