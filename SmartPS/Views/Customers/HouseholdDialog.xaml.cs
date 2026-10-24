using System;
using System.Windows;
using SmartPS.Models.Parking;
using System.Collections.Generic;
using SmartPS.Services.Localization;

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
            if (string.IsNullOrEmpty(ApartmentCode)) { MessageBox.Show(AppText.Get("Msg_Customers_DlgAptRequired")); return; }
            if (MaxVehicles < 1) { MessageBox.Show(AppText.Get("Msg_Customers_DlgAptMaxVehicles"), AppText.Get("Str_Dialog_Title_Info"), MessageBoxButton.OK, MessageBoxImage.Warning); return; }
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
