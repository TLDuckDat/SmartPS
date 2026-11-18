using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SmartPS.Resources.Converters;

/// <summary>
/// Nội dung của ô đang chọn trong ComboBox theo kiểu giao diện dùng chung.
/// Template tự vẽ không có sẵn logic DisplayMemberPath của ComboBox mặc định, nên converter này làm việc đó:
/// - Có ItemTemplate: trả về đối tượng để ItemTemplate vẽ.
/// - Có DisplayMemberPath: trả về giá trị thuộc tính đó (ví dụ TypeName).
/// - Còn lại: chuỗi hoặc ToString() của đối tượng.
/// Thứ tự values: SelectedItem, DisplayMemberPath, ItemTemplate.
/// </summary>
public class ComboBoxSelectionContentConverter : IMultiValueConverter
{
    public object? Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var item = values.Length > 0 ? values[0] : null;
        var path = values.Length > 1 ? values[1] as string : null;
        var itemTemplate = values.Length > 2 ? values[2] : null;

        if (item is null or DependencyProperty) return null;
        if (item is string text) return text;
        // Item khai báo trực tiếp trong XAML (<ComboBoxItem Content="..."/>): hiển thị nội dung của nó
        if (item is System.Windows.Controls.ComboBoxItem comboItem)
        {
            return comboItem.Content is null ? string.Empty : comboItem.Content as string ?? comboItem.Content.ToString();
        }
        if (itemTemplate is not null and not DependencyProperty) return item;

        if (!string.IsNullOrEmpty(path))
        {
            var property = item.GetType().GetProperty(path);
            if (property is not null)
            {
                return property.GetValue(item)?.ToString() ?? string.Empty;
            }
        }

        return item.ToString();
    }

    public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
