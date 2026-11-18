using System.Windows;
using SmartPS.ViewModels.Customers;

namespace SmartPS.Views.Customers;

public partial class HouseholdDialog : Window
{
    public HouseholdDialogViewModel ViewModel { get; }

    public HouseholdDialog(HouseholdDialogViewModel? viewModel = null)
    {
        InitializeComponent();
        ViewModel = viewModel ?? new HouseholdDialogViewModel();
        DataContext = ViewModel;

        ViewModel.RequestClose += result =>
        {
            DialogResult = result;
            Close();
        };
    }

    public string ApartmentCode => ViewModel.ApartmentCode;
    public int? MaxVehicles => ViewModel.MaxVehicles;
    public string Notes => ViewModel.Notes ?? string.Empty;

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
