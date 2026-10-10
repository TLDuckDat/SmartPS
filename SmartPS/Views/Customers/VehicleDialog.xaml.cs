using System.Windows;
using SmartPS.Models.Parking;
using System.Collections.Generic;
using System.Linq;
using System;

namespace SmartPS.Views.Customers
{
    public partial class VehicleDialog : Window
    {
        private List<Customer> _customers;
        private List<VehicleType> _vehicleTypes;

        public string FinalLicensePlate { get; private set; } = string.Empty;
        public int FinalSelectedVehicleType { get; private set; }
        public int FinalSelectedCustomerId { get; private set; }

        public VehicleDialog(List<Customer> customers, List<VehicleType>? vehicleTypes = null, int? preselectedCustomerId = null)
        {
            InitializeComponent();
            _customers = customers;
            _vehicleTypes = vehicleTypes ?? new List<VehicleType>();

            cboCustomer.ItemsSource = _customers;
            cboCustomer.DisplayMemberPath = "FullName";

            if (preselectedCustomerId.HasValue)
            {
                var match = _customers.FirstOrDefault(x => x.CustomerId == preselectedCustomerId.Value);
                if (match != null)
                {
                    cboCustomer.SelectedItem = match;
                }
            }
            if (cboCustomer.SelectedIndex == -1 && _customers.Count > 0)
            {
                cboCustomer.SelectedIndex = 0;
            }

            if (_vehicleTypes.Count > 0)
            {
                cboType.ItemsSource = _vehicleTypes;
                cboType.DisplayMemberPath = "TypeName";
                cboType.SelectedValuePath = "VehicleTypeId";
                cboType.SelectedIndex = 0;
            }
            else
            {
                cboType.ItemsSource = new[]
                {
                    new { TypeName = "Xe máy", VehicleTypeId = 1 },
                    new { TypeName = "Xe ô tô", VehicleTypeId = 2 },
                    new { TypeName = "Xe đạp / Xe điện", VehicleTypeId = 3 }
                };
                cboType.DisplayMemberPath = "TypeName";
                cboType.SelectedValuePath = "VehicleTypeId";
                cboType.SelectedIndex = 0;
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var licensePlate = txtPlate.Text.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(licensePlate)) 
            { 
                MessageBox.Show("Biển số không được để trống", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning); 
                return; 
            }

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

            if (customerId == 0) 
            { 
                MessageBox.Show("Vui lòng chọn chủ xe", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning); 
                return; 
            }

            int vtId = 1;
            if (cboType.SelectedValue is int idVal)
            {
                vtId = idVal;
            }
            else if (cboType.SelectedItem is VehicleType vt)
            {
                vtId = vt.VehicleTypeId;
            }

            // Extract values before closing
            FinalLicensePlate = licensePlate;
            FinalSelectedVehicleType = vtId;
            FinalSelectedCustomerId = customerId;

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
