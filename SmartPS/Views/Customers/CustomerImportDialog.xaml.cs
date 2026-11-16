// Cửa sổ hiển thị preview, tiến độ và thao tác lưu import hộ dân/phương tiện.

using System.ComponentModel;
using System.Windows;
using SmartPS.ViewModels.Customers;

namespace SmartPS.Views.Customers;

public partial class CustomerImportDialog : Window
{
    private readonly CustomerImportViewModel _viewModel;
    private bool _allowClose;

    public CustomerImportDialog(CustomerImportViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;
        _viewModel.RequestClose += OnRequestClose;
    }

    private void OnRequestClose(bool result)
    {
        _allowClose = true;
        DialogResult = result;
        Close();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Close();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        if (_viewModel.IsBusy)
        {
            _viewModel.CancelCommand.Execute(null);
            e.Cancel = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.RequestClose -= OnRequestClose;
        _viewModel.Dispose();
        base.OnClosed(e);
    }
}
