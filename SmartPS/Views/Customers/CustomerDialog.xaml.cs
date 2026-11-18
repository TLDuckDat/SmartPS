using System.Collections.Generic;
using System.Windows;
using SmartPS.Models.Parking;
using SmartPS.ViewModels.Customers;

namespace SmartPS.Views.Customers;

public partial class CustomerDialog : Window
{
    public CustomerDialogViewModel ViewModel { get; }

    public string FinalFullName => ViewModel.FullName;
    public string FinalPhoneNumber => ViewModel.PhoneNumber;
    public int? FinalSelectedHouseholdId => ViewModel.SelectedHouseholdId;
    public string FinalLicensePlate => ViewModel.InitialPlate ?? string.Empty;
    public int FinalSelectedVehicleType => ViewModel.SelectedVehicleType?.VehicleTypeId ?? 1;

    public CustomerDialog(CustomerDialogViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = ViewModel;

        ViewModel.RequestClose += result =>
        {
            DialogResult = result;
            Close();
        };

        if (ViewModel.IsEditing)
        {
            panelVehicle.Visibility = Visibility.Collapsed;
        }
    }

    public CustomerDialog(List<Household> households, List<VehicleType>? vehicleTypes = null, Customer? existing = null)
        : this(new CustomerDialogViewModel(households, vehicleTypes, existing))
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
