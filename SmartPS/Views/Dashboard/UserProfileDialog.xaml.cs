// Cửa sổ chỉnh sửa hồ sơ cá nhân và đổi mật khẩu của người dùng đang đăng nhập.

using System.Windows;
using SmartPS.ViewModels.Dashboard;

namespace SmartPS.Views.Dashboard;

public partial class UserProfileDialog : Window
{
    public UserProfileDialog(UserProfileViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        viewModel.RequestClose += result =>
        {
            DialogResult = result;
            Close();
        };
    }
}
