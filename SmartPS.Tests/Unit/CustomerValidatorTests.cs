using SmartPS.Models.Parking;
using SmartPS.Services.Customers;

namespace SmartPS.Tests.Unit;

/// <summary>R1 / R5 / A14: phone (VN 10 digits), apartment (required for residents), field limits, vehicle rows, type normalization.</summary>
public class CustomerValidatorTests
{
    private static CustomerUpsertRequest Valid(bool resident = true) => new()
    {
        FullName = "Nguyễn Văn Test",
        PhoneNumber = "0988123456",
        Email = "test@example.com",
        IdentityCard = "079123456789",
        IsResident = resident,
        ApartmentCode = resident ? "A-1205" : null,
        Building = resident ? "A" : null,
        Type = resident ? CustomerType.Resident : CustomerType.Regular,
        Notes = "ghi chú",
        Vehicles = new[] { new NewVehicle("51F-123.45", 2), new NewVehicle("59T1-123.45", 1) }
    };

    [Fact]
    public void Valid_resident_and_non_resident_requests_have_no_errors()
    {
        Assert.Empty(CustomerValidator.Validate(Valid(resident: true)));
        Assert.Empty(CustomerValidator.Validate(Valid(resident: false)));
    }

    [Theory]
    [InlineData("0988123456", true)]
    [InlineData("0988.123.456", true)]
    [InlineData("0912 888 999", true)]
    [InlineData("(098) 812-3456", true)]
    [InlineData("0388123456", true)]
    [InlineData("0588123456", true)]
    [InlineData("0788123456", true)]
    [InlineData("012345678", false)]    // 9 digits
    [InlineData("08881234567", false)]  // 11 digits
    [InlineData("0288123456", false)]   // landline prefix
    [InlineData("0188123456", false)]
    [InlineData("1988123456", false)]
    [InlineData("09881234a6", false)]
    [InlineData("", false)]
    public void Phone_rules(string phone, bool valid)
    {
        var errors = CustomerValidator.Validate(Valid() with { PhoneNumber = phone });

        Assert.Equal(!valid, errors.Contains(CustomerValidationError.PhoneInvalid));
    }

    [Theory]
    [InlineData("0988.123.456", "0988123456")]
    [InlineData(" 0988 123 456 ", "0988123456")]
    [InlineData("(098) 812-3456", "0988123456")]
    public void NormalizePhone_strips_separators(string input, string expected)
    {
        Assert.Equal(expected, CustomerValidator.NormalizePhone(input));
    }

    [Fact]
    public void Resident_requires_an_apartment()
    {
        Assert.Contains(CustomerValidationError.ApartmentRequired, CustomerValidator.Validate(Valid() with { ApartmentCode = null }));
        Assert.Contains(CustomerValidationError.ApartmentRequired, CustomerValidator.Validate(Valid() with { ApartmentCode = "   " }));
        Assert.DoesNotContain(CustomerValidationError.ApartmentRequired, CustomerValidator.Validate(Valid(resident: false) with { ApartmentCode = null }));
    }

    [Theory]
    [InlineData("A-1205", true)]
    [InlineData("a-1205", true)]       // upper-cased before matching
    [InlineData("1205", true)]
    [InlineData("B2-A-0101", true)]
    [InlineData("A-1205-B-C", false)]  // more than 3 parts
    [InlineData("A_1205", false)]
    [InlineData("A--1205", false)]
    [InlineData("ABCDEFG-1", false)]   // part longer than 6
    public void Apartment_pattern(string apartment, bool valid)
    {
        var errors = CustomerValidator.Validate(Valid() with { ApartmentCode = apartment });

        Assert.Equal(!valid, errors.Contains(CustomerValidationError.ApartmentInvalid));
    }

    [Fact]
    public void NormalizeApartmentCode_trims_and_upper_cases()
    {
        Assert.Equal("A-1205", CustomerValidator.NormalizeApartmentCode(" a-1205 "));
        Assert.Null(CustomerValidator.NormalizeApartmentCode(null));
        Assert.Null(CustomerValidator.NormalizeApartmentCode("   "));
    }

    [Fact]
    public void Field_limits()
    {
        Assert.Contains(CustomerValidationError.FullNameRequired, CustomerValidator.Validate(Valid() with { FullName = " " }));
        Assert.Contains(CustomerValidationError.FullNameTooLong, CustomerValidator.Validate(Valid() with { FullName = new string('a', 101) }));
        Assert.DoesNotContain(CustomerValidationError.FullNameTooLong, CustomerValidator.Validate(Valid() with { FullName = new string('a', 100) }));
        Assert.Contains(CustomerValidationError.EmailInvalid, CustomerValidator.Validate(Valid() with { Email = "not-an-email" }));
        Assert.Contains(CustomerValidationError.EmailInvalid, CustomerValidator.Validate(Valid() with { Email = new string('a', 95) + "@x.com" }));
        Assert.DoesNotContain(CustomerValidationError.EmailInvalid, CustomerValidator.Validate(Valid() with { Email = null }));
        Assert.Contains(CustomerValidationError.IdentityCardTooLong, CustomerValidator.Validate(Valid() with { IdentityCard = new string('1', 31) }));
        Assert.Contains(CustomerValidationError.BuildingTooLong, CustomerValidator.Validate(Valid() with { Building = new string('B', 51) }));
        Assert.Contains(CustomerValidationError.NotesTooLong, CustomerValidator.Validate(Valid() with { Notes = new string('n', 501) }));
        Assert.DoesNotContain(CustomerValidationError.NotesTooLong, CustomerValidator.Validate(Valid() with { Notes = new string('n', 500) }));
    }

    [Fact]
    public void Vehicle_rows_are_validated()
    {
        Assert.Contains(CustomerValidationError.PlateInvalid,
            CustomerValidator.Validate(Valid() with { Vehicles = new[] { new NewVehicle("AB-1", 1) } }));
        Assert.Contains(CustomerValidationError.VehicleTypeRequired,
            CustomerValidator.Validate(Valid() with { Vehicles = new[] { new NewVehicle("51F-123.45", 0) } }));
        Assert.Contains(CustomerValidationError.DuplicatePlateInRequest,
            CustomerValidator.Validate(Valid() with { Vehicles = new[] { new NewVehicle("51F-123.45", 2), new NewVehicle("51f12345", 2) } }));
        Assert.Empty(CustomerValidator.Validate(Valid() with { Vehicles = Array.Empty<NewVehicle>() }));
    }

    [Theory]
    [InlineData(true, CustomerType.Regular, CustomerType.Resident)]
    [InlineData(true, CustomerType.VIP, CustomerType.Resident)]
    [InlineData(false, CustomerType.Resident, CustomerType.Regular)]
    [InlineData(false, CustomerType.VIP, CustomerType.VIP)]
    [InlineData(false, CustomerType.Loyal, CustomerType.Loyal)]
    public void NormalizeType_forces_resident_type_from_IsResident(bool isResident, CustomerType requested, CustomerType expected)
    {
        Assert.Equal(expected, CustomerValidator.NormalizeType(isResident, requested));
    }

    [Fact]
    public void Patterns_are_the_documented_ones()
    {
        Assert.Equal("^0[35789][0-9]{8}$", CustomerValidator.PhonePattern);
        Assert.Equal("^[A-Z0-9]{1,6}(-[A-Z0-9]{1,6}){0,2}$", CustomerValidator.ApartmentPattern);
    }
}
