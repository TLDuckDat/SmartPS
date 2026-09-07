using System.Windows;
using SmartPS.ViewModels.Dashboard;

namespace SmartPS.Views.Dashboard;

/// <summary>
/// Interaction logic for EditUserDialog.xaml
/// Được tối giản hoàn toàn theo chuẩn MVVM, toàn bộ logic nằm trong EditUserViewModel
/// </summary>
public partial class EditUserDialog : Window
{
    public EditUserViewModel ViewModel { get; }

    public EditUserDialog(EditUserViewModel viewModel)
    {
        InitializeComponent();

        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = ViewModel;

        ViewModel.RequestClose += result =>
        {
            DialogResult = result;
            Close();
        };

        Loaded += (_, _) => FullNameInput.Focus();
    }
}
