using System.Collections.Generic;
using System.Windows;
using SmartPS.Models.Parking;
using SmartPS.ViewModels.Customers;

namespace SmartPS.Views.Customers;

public partial class ChangePlateDialog : Window
{
    public ChangePlateDialogViewModel ViewModel { get; }

    public int FinalNewVehicleId => ViewModel.SelectedNewVehicle?.VehicleId ?? 0;
    public string FinalReason => ViewModel.Reason;

    public ChangePlateDialog(ChangePlateDialogViewModel viewModel)
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

    public ChangePlateDialog(MonthlyTicket ticket, IEnumerable<Vehicle>? customerVehicles = null)
        : this(new ChangePlateDialogViewModel(ticket, customerVehicles))
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
