using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using SmartPS.Models.Parking;
using SmartPS.Services.Localization;

namespace SmartPS.Views.Customers
{
    public partial class AddBlacklistDialog : Window
    {
        private List<Customer> _customers;

        public string FinalLicensePlate { get; private set; } = string.Empty;
        public string FinalReason { get; private set; } = string.Empty;
        public string? FinalNotes { get; private set; }
        public int? FinalCustomerId { get; private set; }

        public AddBlacklistDialog(List<Customer> customers, string? prefillPlate = null)
        {
            InitializeComponent();
            _customers = customers;

            cboCustomer.ItemsSource = _customers;
            cboCustomer.DisplayMemberPath = "FullName";

            if (!string.IsNullOrWhiteSpace(prefillPlate))
            {
                txtPlate.Text = prefillPlate.Trim().ToUpperInvariant();
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var rawPlate = txtPlate.Text.Trim();
            if (string.IsNullOrWhiteSpace(rawPlate))
            {
                MessageBox.Show(AppText.Get("Msg_Customers_DlgBanPlateRequired"), AppText.Get("Str_Customers_DlgMissingInfo"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Chuẩn hóa biển số: viết hoa, loại bỏ ký tự đặc biệt thừa
            var cleanPlate = System.Text.RegularExpressions.Regex.Replace(rawPlate, @"[^a-zA-Z0-9]", "").ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(cleanPlate))
            {
                MessageBox.Show(AppText.Get("Msg_Customers_DlgPlateInvalidShort"), AppText.Get("Str_Dialog_Title_Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (cleanPlate.Length > 20)
            {
                MessageBox.Show(AppText.Get("Msg_Customers_DlgPlateTooLong"), AppText.Get("Str_Dialog_Title_Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var reason = cboReason.Text.Trim();
            if (string.IsNullOrWhiteSpace(reason))
            {
                MessageBox.Show(AppText.Get("Msg_Customers_DlgBanReasonRequired"), AppText.Get("Str_Customers_DlgMissingInfo"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (reason.Length > 250)
            {
                MessageBox.Show(AppText.Get("Msg_Customers_DlgBanReasonTooLong"), AppText.Get("Str_Dialog_Title_Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int? customerId = null;
            if (cboCustomer.SelectedItem is Customer c)
            {
                customerId = c.CustomerId;
            }

            FinalLicensePlate = cleanPlate;
            FinalReason = reason;
            FinalNotes = txtNotes.Text.Trim();
            FinalCustomerId = customerId;

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
