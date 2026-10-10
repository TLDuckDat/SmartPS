using System.Windows;
using SmartPS.Models.Parking;
using System.Collections.Generic;
using System.Linq;
using System;

namespace SmartPS.Views.Customers
{
    public partial class IssueTicketDialog : Window
    {
        private List<Vehicle> _allVehicles;
        private List<Customer> _customers;
        private List<PricingRule> _pricingRules;

        public int FinalSelectedCustomerId { get; private set; }
        public int FinalSelectedVehicleId { get; private set; }
        public int FinalDurationMonths { get; private set; }
        public decimal FinalPrice { get; private set; }

        public IssueTicketDialog(List<Customer> customers, List<Vehicle> vehicles, List<PricingRule>? pricingRules = null, int? preselectedCustomerId = null)
        {
            InitializeComponent();
            _allVehicles = vehicles;
            _customers = customers;
            _pricingRules = pricingRules ?? new List<PricingRule>();
            
            cboCustomer.ItemsSource = _customers;
            cboCustomer.DisplayMemberPath = "FullName";

            if (preselectedCustomerId.HasValue)
            {
                var match = _customers.FirstOrDefault(x => x.CustomerId == preselectedCustomerId.Value);
                if (match != null) cboCustomer.SelectedItem = match;
            }

            UpdatePricePreview();
        }

        private void CboCustomer_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (cboCustomer.SelectedItem is Customer c)
            {
                var userVehicles = _allVehicles.Where(v => v.OwnerCustomerId == c.CustomerId).ToList();
                cboVehicle.ItemsSource = userVehicles;
                cboVehicle.DisplayMemberPath = "LicensePlate";
                if (userVehicles.Count > 0)
                {
                    cboVehicle.SelectedIndex = 0;
                }
            }
            UpdatePricePreview();
        }

        private void CboVehicle_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdatePricePreview();
        }

        private void CboDuration_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdatePricePreview();
        }

        private int GetCurrentDurationMonths()
        {
            return cboDuration.SelectedIndex switch
            {
                0 => 1,
                1 => 3,
                2 => 6,
                _ => 1
            };
        }

        private Vehicle? GetCurrentSelectedVehicle()
        {
            if (cboVehicle.SelectedItem is Vehicle v) return v;
            var text = cboVehicle.Text?.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                var items = cboVehicle.ItemsSource as List<Vehicle>;
                return items?.FirstOrDefault(x => string.Equals(x.LicensePlate, text, StringComparison.OrdinalIgnoreCase));
            }
            return null;
        }

        private void UpdatePricePreview()
        {
            if (txtPricePreview == null) return;

            var v = GetCurrentSelectedVehicle();
            int dur = GetCurrentDurationMonths();

            if (v != null)
            {
                var rule = _pricingRules.FirstOrDefault(r => r.VehicleTypeId == v.VehicleTypeId);
                if (rule != null)
                {
                    decimal price = dur switch
                    {
                        1 => rule.Monthly1Price,
                        3 => rule.Monthly3Price,
                        6 => rule.Monthly6Price,
                        _ => rule.Monthly1Price * dur
                    };
                    FinalPrice = price;
                    txtPricePreview.Text = $"{price:N0} VNĐ";
                    return;
                }
            }

            FinalPrice = 0;
            txtPricePreview.Text = "Chưa chọn phương tiện";
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            int customerId = 0;
            if (cboCustomer.SelectedItem is Customer c) 
            {
                customerId = c.CustomerId;
            }
            else
            {
                var text = cboCustomer.Text?.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    var match = _customers.FirstOrDefault(x => string.Equals(x.FullName, text, StringComparison.OrdinalIgnoreCase));
                    if (match != null) customerId = match.CustomerId;
                }
            }

            var vehicle = GetCurrentSelectedVehicle();
            int vehicleId = vehicle?.VehicleId ?? 0;

            if (customerId == 0) { MessageBox.Show("Chưa chọn khách hàng"); return; }
            if (vehicleId == 0) { MessageBox.Show("Chưa chọn xe"); return; }

            FinalSelectedCustomerId = customerId;
            FinalSelectedVehicleId = vehicleId;
            FinalDurationMonths = GetCurrentDurationMonths();
            UpdatePricePreview();

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
