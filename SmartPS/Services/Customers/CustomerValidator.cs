using System.Text.RegularExpressions;
using SmartPS.Models.Parking;
using SmartPS.Services.GateControl;

namespace SmartPS.Services.Customers;

/// <summary>Kiểm tra dữ liệu khách hàng thuần tuý (không truy cập DB).</summary>
public static class CustomerValidator
{
    public const string PhonePattern = "^0[35789][0-9]{8}$";
    public const string ApartmentPattern = "^[A-Z0-9]{1,6}(-[A-Z0-9]{1,6}){0,2}$";
    public const string EmailPattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";

    private const int MaxFullName = 100;
    private const int MaxEmail = 100;
    private const int MaxIdentityCard = 30;
    private const int MaxBuilding = 50;
    private const int MaxNotes = 500;

    private static readonly char[] PhoneSeparators = { ' ', '.', '-', '(', ')', '\t' };

    /// <summary>Bỏ dấu cách, chấm, gạch ngang và ngoặc khỏi số điện thoại.</summary>
    public static string NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return string.Empty;
        }

        return new string(phone.Where(c => Array.IndexOf(PhoneSeparators, c) < 0).ToArray());
    }

    public static string? NormalizeApartmentCode(string? code)
    {
        var trimmed = code?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.ToUpperInvariant();
    }

    /// <summary>Cư dân luôn có loại Resident; khách không phải cư dân không được mang loại Resident.</summary>
    public static CustomerType NormalizeType(bool isResident, CustomerType requested)
    {
        if (isResident)
        {
            return CustomerType.Resident;
        }

        return requested == CustomerType.Resident ? CustomerType.Regular : requested;
    }

    public static IReadOnlyList<CustomerValidationError> Validate(CustomerUpsertRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new List<CustomerValidationError>();

        var name = request.FullName?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            errors.Add(CustomerValidationError.FullNameRequired);
        }
        else if (name.Length > MaxFullName)
        {
            errors.Add(CustomerValidationError.FullNameTooLong);
        }

        if (!Regex.IsMatch(NormalizePhone(request.PhoneNumber), PhonePattern))
        {
            errors.Add(CustomerValidationError.PhoneInvalid);
        }

        var email = request.Email?.Trim();
        if (!string.IsNullOrEmpty(email) && (email.Length > MaxEmail || !Regex.IsMatch(email, EmailPattern)))
        {
            errors.Add(CustomerValidationError.EmailInvalid);
        }

        if ((request.IdentityCard?.Trim().Length ?? 0) > MaxIdentityCard)
        {
            errors.Add(CustomerValidationError.IdentityCardTooLong);
        }

        var apartment = NormalizeApartmentCode(request.ApartmentCode);
        if (apartment is null)
        {
            if (request.IsResident)
            {
                errors.Add(CustomerValidationError.ApartmentRequired);
            }
        }
        else if (!Regex.IsMatch(apartment, ApartmentPattern))
        {
            errors.Add(CustomerValidationError.ApartmentInvalid);
        }

        if ((request.Building?.Trim().Length ?? 0) > MaxBuilding)
        {
            errors.Add(CustomerValidationError.BuildingTooLong);
        }

        if ((request.Notes?.Trim().Length ?? 0) > MaxNotes)
        {
            errors.Add(CustomerValidationError.NotesTooLong);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var vehicle in request.Vehicles ?? Array.Empty<NewVehicle>())
        {
            var plate = LicensePlateNormalizer.Normalize(vehicle.LicensePlate);
            if (!LicensePlateNormalizer.IsValid(vehicle.LicensePlate))
            {
                errors.Add(CustomerValidationError.PlateInvalid);
            }
            else if (!seen.Add(plate))
            {
                errors.Add(CustomerValidationError.DuplicatePlateInRequest);
            }

            if (vehicle.VehicleTypeId <= 0)
            {
                errors.Add(CustomerValidationError.VehicleTypeRequired);
            }
        }

        return errors.Distinct().ToList();
    }
}
