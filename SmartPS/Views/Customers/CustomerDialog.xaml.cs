using System.Windows;
using SmartPS.Models.Parking;
using System.Collections.Generic;
using System.Linq;
using System;

namespace SmartPS.Views.Customers
{
    public partial class CustomerDialog : Window
    {
        private List<Household> _households;
        private List<VehicleType> _vehicleTypes;

        public string FinalFullName { get; private set; } = string.Empty;
        public string FinalPhoneNumber { get; private set; } = string.Empty;
        public int? FinalSelectedHouseholdId { get; private set; }
        public string FinalLicensePlate { get; private set; } = string.Empty;
        public int FinalSelectedVehicleType { get; private set; }

        public CustomerDialog(List<Household> households, List<VehicleType>? vehicleTypes = null, Customer? existing = null)
        {
            InitializeComponent();
            
            _households = new List<Household>();
            _households.Add(new Household { HouseholdId = -1, ApartmentCode = "-- Khách bên ngoài --" });
            _households.AddRange(households);
            _vehicleTypes = vehicleTypes ?? new List<VehicleType>();
            
            cboHousehold.ItemsSource = _households;
            cboHousehold.DisplayMemberPath = "ApartmentCode";
            cboHousehold.SelectedIndex = 0;

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
            
            if (existing != null)
            {
                Title = "Sửa Khách Hàng";
                txtFullName.Text = existing.FullName;
                txtPhoneNumber.Text = existing.PhoneNumber;
                var match = _households.FirstOrDefault(h => h.HouseholdId == (existing.HouseholdId ?? -1));
                cboHousehold.SelectedItem = match ?? _households[0];
                
                panelVehicle.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtFullName.Text)) 
            { 
                MessageBox.Show("Tên khách hàng không được để trống"); 
                return; 
            }

            // Extract values before closing
            FinalFullName = txtFullName.Text.Trim();
            FinalPhoneNumber = txtPhoneNumber.Text.Trim();
            
            // Logic for SelectedHouseholdId
            string debugInfo = $"SelectedItem is Household: {cboHousehold.SelectedItem is Household}\n";
            if (cboHousehold.SelectedItem is Household h && h.HouseholdId != -1)
            {
                FinalSelectedHouseholdId = h.HouseholdId;
                debugInfo += $"Found from SelectedItem: {h.HouseholdId} ({h.ApartmentCode})\n";
            }
            else
            {
                var text = cboHousehold.Text?.Trim();
                debugInfo += $"Text typed: '{text}'\n";
                if (!string.IsNullOrEmpty(text) && text != "-- Khách bên ngoài --")
                {
                    var match = _households.FirstOrDefault(x => string.Equals(x.ApartmentCode?.Trim(), text, StringComparison.OrdinalIgnoreCase));
                    if (match != null && match.HouseholdId != -1) 
                    {
                        FinalSelectedHouseholdId = match.HouseholdId;
                        debugInfo += $"Found from Text match: {match.HouseholdId} ({match.ApartmentCode})\n";
                    }
                    else 
                    {
                        FinalSelectedHouseholdId = null;
                        debugInfo += $"No match found for text.\n";
                    }
                }
                else
                {
                    FinalSelectedHouseholdId = null;
                    debugInfo += $"Text is empty or external.\n";
                }
            }
            
            // MessageBox.Show(debugInfo, "Debug Info");

            FinalLicensePlate = txtPlate.Text.Trim().ToUpperInvariant();
            
            int vtId = 1;
            if (cboType.SelectedValue is int idVal)
            {
                vtId = idVal;
            }
            else if (cboType.SelectedItem is VehicleType vt)
            {
                vtId = vt.VehicleTypeId;
            }
            FinalSelectedVehicleType = vtId;

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
