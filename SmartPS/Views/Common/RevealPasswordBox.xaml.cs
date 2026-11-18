using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SmartPS.Views.Common;

/// <summary>
/// Control ô nhập mật khẩu hỗ trợ nút bật/tắt (Ẩn/Hiện) kiểm tra mật khẩu
/// </summary>
public partial class RevealPasswordBox : UserControl
{
    private const string IconDataEye = "M2,12 C4.2,7.5 7.8,5 12,5 C16.2,5 19.8,7.5 22,12 C19.8,16.5 16.2,19 12,19 C7.8,19 4.2,16.5 2,12 Z M12,15 A3,3 0 1,0 12,9 A3,3 0 1,0 12,15 Z";
    private const string IconDataEyeOff = "M3,3 L21,21 M10.6,10.6 A2,2 0 0,0 13.4,13.4 M9.9,5.2 C10.6,5.1 11.3,5 12,5 C16.2,5 19.8,7.5 22,12 C21.1,13.8 19.9,15.3 18.4,16.5 M6.2,6.2 C4.4,7.5 3,9.5 2,12 C4.2,16.5 7.8,19 12,19 C13.8,19 15.5,18.5 17,17.7";

    private bool _isUpdating;
    private bool _isRevealed;

    #region Dependency Properties

    public static readonly DependencyProperty PasswordProperty =
        DependencyProperty.Register(
            nameof(Password),
            typeof(string),
            typeof(RevealPasswordBox),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPasswordPropertyChanged));

    public string Password
    {
        get => (string)GetValue(PasswordProperty);
        set => SetValue(PasswordProperty, value);
    }

    public static readonly DependencyProperty InputBackgroundProperty =
        DependencyProperty.Register(nameof(InputBackground), typeof(Brush), typeof(RevealPasswordBox), new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF))));

    public Brush InputBackground
    {
        get => (Brush)GetValue(InputBackgroundProperty);
        set => SetValue(InputBackgroundProperty, value);
    }

    public static readonly DependencyProperty InputBorderBrushProperty =
        DependencyProperty.Register(nameof(InputBorderBrush), typeof(Brush), typeof(RevealPasswordBox), new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0xDF, 0xC7, 0xB9))));

    public Brush InputBorderBrush
    {
        get => (Brush)GetValue(InputBorderBrushProperty);
        set => SetValue(InputBorderBrushProperty, value);
    }

    public static readonly DependencyProperty InputFocusedBorderBrushProperty =
        DependencyProperty.Register(nameof(InputFocusedBorderBrush), typeof(Brush), typeof(RevealPasswordBox), new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0xE8, 0x73, 0x5A))));

    public Brush InputFocusedBorderBrush
    {
        get => (Brush)GetValue(InputFocusedBorderBrushProperty);
        set => SetValue(InputFocusedBorderBrushProperty, value);
    }

    public static readonly DependencyProperty InputForegroundProperty =
        DependencyProperty.Register(nameof(InputForeground), typeof(Brush), typeof(RevealPasswordBox), new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x3A, 0x2A, 0x3C))));

    public Brush InputForeground
    {
        get => (Brush)GetValue(InputForegroundProperty);
        set => SetValue(InputForegroundProperty, value);
    }

    public static readonly DependencyProperty InputBorderThicknessProperty =
        DependencyProperty.Register(nameof(InputBorderThickness), typeof(Thickness), typeof(RevealPasswordBox), new PropertyMetadata(new Thickness(1)));

    public Thickness InputBorderThickness
    {
        get => (Thickness)GetValue(InputBorderThicknessProperty);
        set => SetValue(InputBorderThicknessProperty, value);
    }

    public static readonly DependencyProperty InputCornerRadiusProperty =
        DependencyProperty.Register(nameof(InputCornerRadius), typeof(CornerRadius), typeof(RevealPasswordBox), new PropertyMetadata(new CornerRadius(4)));

    public CornerRadius InputCornerRadius
    {
        get => (CornerRadius)GetValue(InputCornerRadiusProperty);
        set => SetValue(InputCornerRadiusProperty, value);
    }

    public static readonly DependencyProperty InputHeightProperty =
        DependencyProperty.Register(nameof(InputHeight), typeof(double), typeof(RevealPasswordBox), new PropertyMetadata(36.0));

    public double InputHeight
    {
        get => (double)GetValue(InputHeightProperty);
        set => SetValue(InputHeightProperty, value);
    }

    public static readonly DependencyProperty InputFontSizeProperty =
        DependencyProperty.Register(nameof(InputFontSize), typeof(double), typeof(RevealPasswordBox), new PropertyMetadata(13.0));

    public double InputFontSize
    {
        get => (double)GetValue(InputFontSizeProperty);
        set => SetValue(InputFontSizeProperty, value);
    }

    #endregion

    public RevealPasswordBox()
    {
        InitializeComponent();
        UpdateEyeIcon();
    }

    private static void OnPasswordPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RevealPasswordBox control && !control._isUpdating)
        {
            var newPass = e.NewValue as string ?? string.Empty;
            control.SyncPasswordFromProperty(newPass);
        }
    }

    private void SyncPasswordFromProperty(string pass)
    {
        _isUpdating = true;
        try
        {
            if (MaskedPasswordBox.Password != pass)
            {
                MaskedPasswordBox.Password = pass;
            }
            if (UnmaskedTextBox.Text != pass)
            {
                UnmaskedTextBox.Text = pass;
            }
        }
        finally
        {
            _isUpdating = false;
        }
    }

    private void MaskedPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_isUpdating) return;

        _isUpdating = true;
        try
        {
            Password = MaskedPasswordBox.Password;
            UnmaskedTextBox.Text = MaskedPasswordBox.Password;
        }
        finally
        {
            _isUpdating = false;
        }
    }

    private void UnmaskedTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdating) return;

        _isUpdating = true;
        try
        {
            Password = UnmaskedTextBox.Text;
            MaskedPasswordBox.Password = UnmaskedTextBox.Text;
        }
        finally
        {
            _isUpdating = false;
        }
    }

    private void EyeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _isRevealed = !_isRevealed;

        if (_isRevealed)
        {
            // Chuyển sang chế độ Hiện mật khẩu
            UnmaskedTextBox.Text = MaskedPasswordBox.Password;
            MaskedPasswordBox.Visibility = Visibility.Collapsed;
            UnmaskedTextBox.Visibility = Visibility.Visible;

            if (MaskedPasswordBox.IsFocused)
            {
                UnmaskedTextBox.Focus();
                UnmaskedTextBox.CaretIndex = UnmaskedTextBox.Text.Length;
            }
        }
        else
        {
            // Chuyển sang chế độ Ẩn mật khẩu
            MaskedPasswordBox.Password = UnmaskedTextBox.Text;
            UnmaskedTextBox.Visibility = Visibility.Collapsed;
            MaskedPasswordBox.Visibility = Visibility.Visible;

            if (UnmaskedTextBox.IsFocused)
            {
                MaskedPasswordBox.Focus();
            }
        }

        UpdateEyeIcon();
    }

    private void UpdateEyeIcon()
    {
        if (_isRevealed)
        {
            EyeIconPath.Data = Geometry.Parse(IconDataEyeOff);
            EyeIconPath.Stroke = new SolidColorBrush(Color.FromRgb(0xE8, 0x73, 0x5A)); // Active coral highlight
            EyeToggleButton.SetResourceReference(ToolTipProperty, "Str_Password_Hide");
        }
        else
        {
            EyeIconPath.Data = Geometry.Parse(IconDataEye);
            EyeIconPath.Stroke = new SolidColorBrush(Color.FromRgb(0x7A, 0x66, 0x7C)); // Neutral warm muted
            EyeToggleButton.SetResourceReference(ToolTipProperty, "Str_Password_Show");
        }
    }

    private void InputControl_GotFocus(object sender, RoutedEventArgs e)
    {
        MainBorder.BorderBrush = InputFocusedBorderBrush;
    }

    private void InputControl_LostFocus(object sender, RoutedEventArgs e)
    {
        MainBorder.BorderBrush = InputBorderBrush;
    }

    private void InputControl_KeyDown(object sender, KeyEventArgs e)
    {
        // Chuyển tiếp sự kiện phím (ví dụ Enter) lên cho parent xử lý nếu cần
        RaiseEvent(new KeyEventArgs(e.KeyboardDevice, e.InputSource, e.Timestamp, e.Key)
        {
            RoutedEvent = KeyDownEvent
        });
    }

    public new bool Focus()
    {
        return _isRevealed ? UnmaskedTextBox.Focus() : MaskedPasswordBox.Focus();
    }

    public void Clear()
    {
        Password = string.Empty;
        MaskedPasswordBox.Password = string.Empty;
        UnmaskedTextBox.Text = string.Empty;
    }
}
