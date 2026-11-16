// Các kiểu dữ liệu dùng chung cho luồng import hộ dân và phương tiện.

using SmartPS.Services.Localization;

namespace SmartPS.DTOs.Customers;

public enum CustomerImportRowStatus
{
    Valid = 0,
    Warning = 1,
    Error = 2
}

public sealed record CustomerImportRecord(
    int RowNumber,
    string ApartmentCode,
    string FullName,
    string PhoneNumber,
    string? Email,
    string? IdentityCard,
    string LicensePlate,
    string VehicleTypeName,
    int VehicleTypeId,
    string? Color,
    string? Brand,
    int? MaxVehicles,
    string? Notes);

public sealed class CustomerImportRowResult
{
    public int RowNumber { get; init; }
    public string ApartmentCode { get; init; } = string.Empty;
    public string OwnerName { get; init; } = string.Empty;
    public string LicensePlate { get; init; } = string.Empty;
    public string VehicleTypeName { get; init; } = string.Empty;
    public CustomerImportRowStatus Status { get; init; }
    public string Message { get; init; } = string.Empty;
    public CustomerImportRecord? Record { get; init; }

    public string StatusText => Status switch
    {
        CustomerImportRowStatus.Valid => AppText.Get("Str_Customers_ImportStatusValid"),
        CustomerImportRowStatus.Warning => AppText.Get("Str_Customers_ImportStatusWarning"),
        _ => AppText.Get("Str_Customers_ImportStatusError")
    };

    public bool IsError => Status == CustomerImportRowStatus.Error;
}

public sealed class CustomerImportPreview
{
    public IReadOnlyList<CustomerImportRowResult> Rows { get; init; } = Array.Empty<CustomerImportRowResult>();
    public IReadOnlyList<CustomerImportRecord> Records { get; init; } = Array.Empty<CustomerImportRecord>();

    public int TotalRows => Rows.Count;
    public int ValidRows => Rows.Count(x => !x.IsError);
    public int ErrorRows => Rows.Count(x => x.IsError);
    public int WarningRows => Rows.Count(x => x.Status == CustomerImportRowStatus.Warning);
    public bool CanSave => TotalRows > 0 && ErrorRows == 0;
}

public sealed class CustomerImportProgress
{
    public string Phase { get; init; } = string.Empty;
    public int ProcessedRows { get; init; }
    public int TotalRows { get; init; }
    public int Percent => TotalRows <= 0 ? 0 : Math.Clamp(ProcessedRows * 100 / TotalRows, 0, 100);
}

public sealed class CustomerImportSaveResult
{
    public int CreatedHouseholds { get; init; }
    public int CreatedCustomers { get; init; }
    public int CreatedVehicles { get; init; }
    public int SkippedVehicles { get; init; }
}
