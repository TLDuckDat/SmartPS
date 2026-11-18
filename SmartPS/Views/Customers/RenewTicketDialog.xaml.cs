using System;
using System.Windows;
using SmartPS.Models.Parking;
using SmartPS.ViewModels.Customers;

namespace SmartPS.Views.Customers;

public partial class RenewTicketDialog : Window
{
    public RenewTicketDialogViewModel ViewModel { get; }

    public int FinalDurationMonths => ViewModel.AddedMonths;
    public decimal FinalFee => ViewModel.EstimatedFee;
    public DateTime FinalNewExpiry => ViewModel.NewEndDate;
    public PaymentMethod FinalPaymentMethod => ViewModel.SelectedPaymentMethod;

    public RenewTicketDialog(RenewTicketDialogViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = ViewModel;

        ViewModel.RequestClose += result =>
        {
            DialogResult = result;
            Close();
        };

        cboDuration.SelectedIndex = ViewModel.AddedMonths switch
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

    public RenewTicketDialog(MonthlyTicket ticket, PricingRule? rule)
        : this(new RenewTicketDialogViewModel(ticket, rule))
    {
    }

    private void CboDuration_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ViewModel == null) return;
        ViewModel.AddedMonths = cboDuration.SelectedIndex switch
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
