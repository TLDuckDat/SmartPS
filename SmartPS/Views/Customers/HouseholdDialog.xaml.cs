using System;
using System.Windows;
using SmartPS.Models.Parking;
using System.Collections.Generic;

namespace SmartPS.Views.Customers
{
    public partial class HouseholdDialog : Window
    {
        public HouseholdDialog()
        {
            InitializeComponent();
        }

        public string ApartmentCode => txtApartmentCode.Text.Trim();
        public int MaxVehicles => int.TryParse(txtMaxVehicles.Text, out int m) ? m : 2;
        public string Notes => txtNotes.Text.Trim();

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(ApartmentCode)) { MessageBox.Show("Mã căn hộ không được để trống"); return; }
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
