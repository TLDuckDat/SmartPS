using System.Collections.Generic;
using System.Windows;
using SmartPS.Models.Parking;
using SmartPS.ViewModels.Customers;

namespace SmartPS.Views.Customers;

public partial class IssueTicketDialog : Window
{
    public IssueTicketDialogViewModel ViewModel { get; }

    public int FinalSelectedCustomerId => ViewModel.SelectedCustomer?.CustomerId ?? 0;
    public int FinalSelectedVehicleId => ViewModel.SelectedVehicle?.VehicleId ?? 0;
    public int FinalDurationMonths => ViewModel.DurationMonths;
    public decimal FinalPrice => ViewModel.EstimatedFeePreview;
    public PaymentMethod FinalPaymentMethod => ViewModel.SelectedPaymentMethod;

    public IssueTicketDialog(IssueTicketDialogViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = ViewModel;

        ViewModel.RequestClose += result =>
        {
            DialogResult = result;
            Close();
        };

        // Đồng bộ thời hạn ban đầu
        cboDuration.SelectedIndex = ViewModel.DurationMonths switch
        {
            3 => 1,
            6 => 2,
            _ => 0
        };

        cboPaymentMethod.SelectedIndex = ViewModel.SelectedPaymentMethod switch
        {
            PaymentMethod.VietQR => 1,
            PaymentMethod.Card => 2,
            _ => 0
        };
    }

    public IssueTicketDialog(List<Customer> customers, List<Vehicle> vehicles, List<PricingRule>? pricingRules = null, int? preselectedCustomerId = null)
        : this(new IssueTicketDialogViewModel(customers, vehicles, pricingRules, preselectedCustomerId))
    {
    }

    private void CboDuration_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ViewModel == null) return;
        ViewModel.DurationMonths = cboDuration.SelectedIndex switch
        {
            1 => 3,
            2 => 6,
            _ => 1
        };
    }

    private void CboPaymentMethod_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ViewModel == null) return;
        ViewModel.SelectedPaymentMethod = cboPaymentMethod.SelectedIndex switch
        {
            1 => PaymentMethod.VietQR,
            2 => PaymentMethod.Card,
            _ => PaymentMethod.Cash
        };
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
