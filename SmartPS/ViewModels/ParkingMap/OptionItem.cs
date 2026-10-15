namespace SmartPS.ViewModels.ParkingMap;

/// <summary>Giá trị chọn trong ComboBox với nhãn đã dịch.</summary>
public sealed class OptionItem<T>
{
    public OptionItem(T value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }

    public T Value { get; }

    public string DisplayName { get; }
}
