using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using SmartPS.Models.Parking;

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
                MessageBox.Show("Vui lòng nhập biển số xe bị cấm!", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Chuẩn hóa biển số: viết hoa, loại bỏ ký tự đặc biệt thừa
            var cleanPlate = rawPlate.ToUpperInvariant().Replace(" ", "").Replace("-", "").Replace(".", "");
            if (string.IsNullOrWhiteSpace(cleanPlate))
            {
                MessageBox.Show("Biển số không hợp lệ!", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var reason = cboReason.Text.Trim();
            if (string.IsNullOrWhiteSpace(reason))
            {
                MessageBox.Show("Vui lòng nhập hoặc chọn lý do cấm!", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int? customerId = null;
            if (cboCustomer.SelectedItem is Customer c)
            {
                customerId = c.CustomerId;
            }

            FinalLicensePlate = rawPlate.ToUpperInvariant();
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
