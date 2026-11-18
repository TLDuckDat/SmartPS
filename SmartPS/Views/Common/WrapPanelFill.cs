using System.Windows;
using System.Windows.Controls;

namespace SmartPS.Views.Common;

/// <summary>
/// Khiến WrapPanel lấp đầy chiều rộng: số cột = floor(chiều rộng / MinItemWidth), rồi chia đều chiều rộng cho các cột.
/// Nhờ vậy lưới thẻ không để trống mép phải khi cửa sổ thay đổi kích thước.
/// </summary>
public static class WrapPanelFill
{
    public static readonly DependencyProperty MinItemWidthProperty =
        DependencyProperty.RegisterAttached(
            "MinItemWidth",
            typeof(double),
            typeof(WrapPanelFill),
            new PropertyMetadata(0d, OnMinItemWidthChanged));

    public static double GetMinItemWidth(DependencyObject element) => (double)element.GetValue(MinItemWidthProperty);

    public static void SetMinItemWidth(DependencyObject element, double value) => element.SetValue(MinItemWidthProperty, value);

    private static void OnMinItemWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not WrapPanel panel) return;

        panel.SizeChanged -= OnPanelSizeChanged;
        panel.SizeChanged += OnPanelSizeChanged;
        Recalculate(panel);
    }

    private static void OnPanelSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is WrapPanel panel) Recalculate(panel);
    }

    private static void Recalculate(WrapPanel panel)
    {
        var minWidth = GetMinItemWidth(panel);
        if (minWidth <= 0 || panel.ActualWidth <= 0) return;

        var columns = Math.Max(1, (int)Math.Floor(panel.ActualWidth / minWidth));
        var itemWidth = Math.Floor(panel.ActualWidth / columns);
        if (Math.Abs(panel.ItemWidth - itemWidth) > 0.5)
        {
            panel.ItemWidth = itemWidth;
        }
    }
}
