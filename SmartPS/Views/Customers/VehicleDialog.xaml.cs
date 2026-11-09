using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using SmartPS.Models.Parking;
using SmartPS.ViewModels.Customers;

namespace SmartPS.Views.Customers;

public partial class VehicleDialog : Window
{
    public VehicleDialogViewModel ViewModel { get; }

    public string FinalLicensePlate => Regex.Replace(ViewModel.LicensePlate?.Trim() ?? string.Empty, @"[^a-zA-Z0-9]", "").ToUpperInvariant();
    public int FinalSelectedVehicleType => ViewModel.SelectedVehicleType?.VehicleTypeId ?? 1;
    public int FinalSelectedCustomerId => ViewModel.SelectedCustomer?.CustomerId ?? 0;

    public VehicleDialog(VehicleDialogViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = ViewModel;

        ViewModel.RequestClose += result =>
        {
            DialogResult = result;
            Close();
        };
    }

    public VehicleDialog(List<Customer> customers, List<VehicleType>? vehicleTypes = null, int? preselectedCustomerId = null)
        : this(new VehicleDialogViewModel(customers, vehicleTypes, preselectedCustomerId))
    {
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.Validate())
        {
            if (!string.IsNullOrEmpty(ViewModel.ErrorMessage))
            {
                MessageBox.Show(ViewModel.ErrorMessage, "SmartPS", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return;
        }
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
