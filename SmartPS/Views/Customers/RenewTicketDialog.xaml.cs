using System.Windows;
using SmartPS.Models.Parking;
using System;

namespace SmartPS.Views.Customers
{
    public partial class RenewTicketDialog : Window
    {
        private MonthlyTicket _ticket;
        private PricingRule? _rule;

        public int FinalDurationMonths { get; private set; }
        public decimal FinalFee { get; private set; }
        public DateTime FinalNewExpiry { get; private set; }

        public RenewTicketDialog(MonthlyTicket ticket, PricingRule? rule)
        {
            InitializeComponent();
            _ticket = ticket;
            _rule = rule;

            txtTicketCode.Text = ticket.TicketCode;
            var customerName = ticket.Customer?.FullName ?? "N/A";
            var plate = ticket.RegisteredLicensePlate;
            txtCustomerInfo.Text = $"{customerName} ({plate})";
            txtCurrentExpiry.Text = ticket.EndDate.ToString("dd/MM/yyyy HH:mm");

            UpdatePreview();
        }

        private void CboDuration_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdatePreview();
        }

        private int GetSelectedDuration()
        {
            return cboDuration.SelectedIndex switch
            {
                0 => 1,
                1 => 3,
                2 => 6,
                _ => 1
            };
        }

        private void UpdatePreview()
        {
            if (txtFee == null || txtNewExpiry == null) return;

            int dur = GetSelectedDuration();
            decimal fee = 0;
            if (_rule != null)
            {
                fee = dur switch
                {
                    1 => _rule.Monthly1Price,
                    3 => _rule.Monthly3Price,
                    6 => _rule.Monthly6Price,
                    _ => _rule.Monthly1Price * dur
                };
            }
            else
            {
                fee = 200000m * dur;
            }

            DateTime baseDate = _ticket.EndDate < DateTime.UtcNow ? DateTime.UtcNow : _ticket.EndDate;
            DateTime newExp = baseDate.AddMonths(dur);

            FinalDurationMonths = dur;
            FinalFee = fee;
            FinalNewExpiry = newExp;

            txtFee.Text = $"{fee:N0} VNĐ";
            txtNewExpiry.Text = newExp.ToString("dd/MM/yyyy HH:mm");
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            UpdatePreview();
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
