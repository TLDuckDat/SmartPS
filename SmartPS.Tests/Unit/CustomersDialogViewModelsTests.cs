using System;
using System.Collections.Generic;
using SmartPS.Models.Parking;
using SmartPS.Services.Customers;
using SmartPS.ViewModels.Customers;
using Xunit;

namespace SmartPS.Tests.Unit;

public class CustomersDialogViewModelsTests
{
    [Fact]
    public void HouseholdDialogViewModel_ValidationAndToRequest()
    {
        var vm = new HouseholdDialogViewModel();
        Assert.Null(vm.MaxVehicles); // Mặc định để null để fallback theo cấu hình hệ thống

        Assert.False(vm.Validate());
        Assert.NotNull(vm.ErrorMessage);

        vm.ApartmentCode = "P.1001";
        // Khi để null, hợp lệ và nhận null
        Assert.True(vm.Validate());
        var reqDefault = vm.ToRequest();
        Assert.Equal("P.1001", reqDefault.ApartmentCode);
        Assert.Null(reqDefault.MaxVehicles);

        vm.MaxVehicles = -1;
        Assert.False(vm.Validate());

        vm.MaxVehicles = 3;
        vm.Notes = "Căn hộ mẫu";
        Assert.True(vm.Validate());

        var req = vm.ToRequest();
        Assert.Equal("P.1001", req.ApartmentCode);
        Assert.Equal(3, req.MaxVehicles);
        Assert.Equal("Căn hộ mẫu", req.Notes);
    }

    [Fact]
    public void CustomerDialogViewModel_ValidationAndToRequest()
    {
        var hhs = new List<Household>
        {
            new Household { HouseholdId = 1, ApartmentCode = "A.101" }
        };
        var vm = new CustomerDialogViewModel(hhs);

        // Chưa nhập họ tên
        Assert.False(vm.Validate());

        vm.FullName = "Trần Văn A";
        vm.PhoneNumber = "0901234567";
        vm.SelectedHousehold = hhs[0];
        vm.InitialPlate = "30A-999.88";
        Assert.True(vm.Validate());

        var req = vm.ToRequest();
        Assert.Equal("Trần Văn A", req.FullName);
        Assert.Equal("0901234567", req.PhoneNumber);
        Assert.Equal(1, req.HouseholdId);
        Assert.Equal("30A-999.88", req.InitialPlate);
    }

    [Fact]
    public void VehicleDialogViewModel_NormalizesPlateAndValidates()
    {
        var customers = new List<Customer>
        {
            new Customer { CustomerId = 1, FullName = "Lê Thị B" }
        };
        var vm = new VehicleDialogViewModel(customers);

        vm.LicensePlate = "  29-b1 123.45  ";
        Assert.True(vm.Validate());

        var req = vm.ToRequest();
        Assert.Equal(1, req.CustomerId);
        Assert.Equal("29B112345", req.LicensePlate); // Chuẩn hóa hoa, không dấu phân cách
    }

    [Fact]
    public void IssueTicketDialogViewModel_CalculatesPreviewFeeAndGeneratesRequest()
    {
        var customer = new Customer { CustomerId = 10, FullName = "Khách Hàng C" };
        var vehicle = new Vehicle { VehicleId = 20, LicensePlate = "29A11111", VehicleTypeId = 1, OwnerCustomerId = 10, IsActive = true };
        var rules = new List<PricingRule>
        {
            new PricingRule { VehicleTypeId = 1, Monthly1Price = 100000m, Monthly3Price = 285000m, Monthly6Price = 540000m }
        };

        var vm = new IssueTicketDialogViewModel(new[] { customer }, new[] { vehicle }, rules);
        Assert.Equal(1, vm.DurationMonths);
        Assert.Equal(100000m, vm.EstimatedFeePreview);

        vm.DurationMonths = 3;
        Assert.Equal(285000m, vm.EstimatedFeePreview);

        vm.DurationMonths = 6;
        Assert.Equal(540000m, vm.EstimatedFeePreview);

        vm.SelectedPaymentMethod = PaymentMethod.VietQR;
        Assert.True(vm.Validate());

        var req = vm.ToRequest();
        Assert.Equal(10, req.CustomerId);
        Assert.Equal(20, req.VehicleId);
        Assert.Equal(6, req.DurationMonths);
        Assert.Equal(PaymentMethod.VietQR, req.PaymentMethod);
    }

    [Fact]
    public void RenewTicketDialogViewModel_CalculatesPreviewAndGeneratesRequest()
    {
        var now = DateTime.UtcNow;
        var ticket = new MonthlyTicket
        {
            TicketId = 99,
            TicketCode = "MT-2026-0001",
            RegisteredLicensePlate = "30E12345",
            VehicleTypeId = 2,
            EndDate = now.AddDays(5)
        };
        var rule = new PricingRule { VehicleTypeId = 2, Monthly1Price = 1200000m, Monthly3Price = 3400000m };

        var vm = new RenewTicketDialogViewModel(ticket, rule);
        Assert.Equal(1, vm.AddedMonths);
        Assert.Equal(1200000m, vm.EstimatedFee);

        vm.AddedMonths = 3;
        Assert.Equal(3400000m, vm.EstimatedFee);

        vm.SelectedPaymentMethod = PaymentMethod.Card;
        Assert.True(vm.Validate());

        var req = vm.ToRequest();
        Assert.Equal(99, req.TicketId);
        Assert.Equal(3, req.AddedMonths);
        Assert.Equal(PaymentMethod.Card, req.PaymentMethod);
    }

    [Fact]
    public void ChangePlateDialogViewModel_ValidatesNewVehicleAndGeneratesRequest()
    {
        var ticket = new MonthlyTicket
        {
            TicketId = 101,
            TicketCode = "MT-CHANGE-01",
            VehicleId = 1,
            RegisteredLicensePlate = "29A11111"
        };
        var vehicle2 = new Vehicle { VehicleId = 2, LicensePlate = "29A22222", IsActive = true };

        var vm = new ChangePlateDialogViewModel(ticket, new[] { vehicle2 });
        Assert.NotNull(vm.SelectedNewVehicle);
        Assert.Equal(2, vm.SelectedNewVehicle.VehicleId);
        Assert.True(vm.Validate());

        vm.Reason = "Đã bán xe cũ mua xe mới";
        var req = vm.ToRequest();
        Assert.Equal(101, req.TicketId);
        Assert.Equal(2, req.NewVehicleId);
        Assert.Equal("Đã bán xe cũ mua xe mới", req.Reason);
    }
}
