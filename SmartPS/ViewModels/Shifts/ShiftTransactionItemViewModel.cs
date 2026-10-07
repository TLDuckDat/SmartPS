using SmartPS.Models.Parking;
using SmartPS.Models.Shifts;
using SmartPS.Services.Localization;
using SmartPS.ViewModels;

namespace SmartPS.ViewModels.Shifts;

public class ShiftTransactionItemViewModel : ViewModelBase
{
    private readonly ILocalizationService? _localizationService;
    public FinancialTransaction Transaction { get; }

    public int TransactionId => Transaction.TransactionId;
    public string TransactionCode => Transaction.TransactionCode;
    public DateTime CreatedAtLocal => Transaction.CreatedAt.Kind == DateTimeKind.Utc
        ? Transaction.CreatedAt.ToLocalTime()
        : Transaction.CreatedAt;
    public string LicensePlate => Transaction.ParkingSession?.LicensePlate ?? "--";
    public string CreatedBy => Transaction.CreatedByUser?.FullName ?? $"User #{Transaction.CreatedByUserId}";
    public string ReferenceCode => string.IsNullOrWhiteSpace(Transaction.ReferenceCode) ? "--" : Transaction.ReferenceCode!;
    public string Note => string.IsNullOrWhiteSpace(Transaction.Note) ? "--" : Transaction.Note!;
    public decimal Amount => Transaction.Amount;
    public string AmountFormatted => $"{Transaction.Amount:N0} đ";
    public string LocalizedAmountFormatted => _localizationService?.GetString("Str_Shifts_CurrencyFormat", Transaction.Amount) ?? AmountFormatted;

    public string LocalizedTypeText => _localizationService?.GetString(Transaction.Type switch
    {
        FinancialTransactionType.ParkingFee => "Str_Shifts_TypeParkingFee",
        FinancialTransactionType.Refund => "Str_Shifts_TypeRefund",
        FinancialTransactionType.Adjustment => "Str_Shifts_TypeAdjustment",
        FinancialTransactionType.Incident => "Str_Shifts_TypeIncident",
        _ => string.Empty
    }) ?? TypeText;

    public string LocalizedPaymentMethodText => _localizationService?.GetString(Transaction.PaymentMethod switch
    {
        PaymentMethod.Cash => "Str_Shifts_PaymentCash",
        PaymentMethod.VietQR => "Str_Shifts_PaymentVietQr",
        PaymentMethod.Card => "Str_Shifts_PaymentCard",
        PaymentMethod.Free => "Str_Shifts_PaymentFree",
        _ => string.Empty
    }) ?? PaymentMethodText;

    public string TypeText => Transaction.Type switch
    {
        FinancialTransactionType.ParkingFee => "Phí gửi xe",
        FinancialTransactionType.Refund => "Hoàn tiền",
        FinancialTransactionType.Adjustment => "Điều chỉnh",
        FinancialTransactionType.Incident => "Liên quan sự cố",
        _ => Transaction.Type.ToString()
    };

    public string PaymentMethodText => Transaction.PaymentMethod switch
    {
        PaymentMethod.Cash => "Tiền mặt",
        PaymentMethod.VietQR => "VietQR",
        PaymentMethod.Card => "Thẻ / Ví",
        PaymentMethod.Free => "Miễn phí",
        _ => Transaction.PaymentMethod.ToString()
    };

    public ShiftTransactionItemViewModel(FinancialTransaction transaction, ILocalizationService? localizationService = null)
    {
        Transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
        _localizationService = localizationService;
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(LocalizedAmountFormatted));
        OnPropertyChanged(nameof(LocalizedTypeText));
        OnPropertyChanged(nameof(LocalizedPaymentMethodText));
    }
}
