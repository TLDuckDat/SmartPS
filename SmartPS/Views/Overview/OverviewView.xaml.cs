using System.Windows;
using System.Windows.Controls;
using SmartPS.ViewModels.Overview;

namespace SmartPS.Views.Overview;

/// <summary>
/// Interaction logic for OverviewView.xaml
/// </summary>
public partial class OverviewView : UserControl
{
    public OverviewView()
    {
        InitializeComponent();

        // Phí tạm tính chỉ cập nhật khi màn hình đang hiển thị
        Loaded += (_, _) => (DataContext as OverviewViewModel)?.StartLiveFees();
        Unloaded += (_, _) => (DataContext as OverviewViewModel)?.StopLiveFees();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        (e.OldValue as OverviewViewModel)?.StopLiveFees();
        if (IsLoaded)
        {
            (e.NewValue as OverviewViewModel)?.StartLiveFees();
        }
    }
}
