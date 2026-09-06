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
    private const string IconDataEye = "M12 4.5C7 4.5 2.73 7.61 1 12c1.73 4.39 6 7.5 11 7.5s9.27-3.11 11-7.5c-1.73-4.39-6-7.5-11-7.5zM12 17c-2.76 0-5-2.24-5-5s2.24-5 5-5 5 2.24 5 5-2.24 5-5 5zm0-8c-1.66 0-3 1.34-3 3s1.34 3 3 3 3-1.34 3-3-1.34-3-3-3z";
    private const string IconDataEyeOff = "M12 7c2.76 0 5 2.24 5 5 0 .65-.13 1.26-.36 1.83l2.92 2.92c1.51-1.26 2.7-2.89 3.44-4.75-1.73-4.39-6-7.5-11-7.5-1.4 0-2.74.25-3.98.7l2.16 2.16C10.74 7.13 11.35 7 12 7zM2 4.27l2.28 2.28.46.46C3.08 8.3 1.78 10.02 1 12c1.73 4.39 6 7.5 11 7.5 1.55 0 3.03-.3 4.38-.84l.42.42L19.73 22 21 20.73 3.27 3 2 4.27zM7.53 9.8l1.55 1.55c-.05.21-.08.43-.08.65 0 1.66 1.34 3 3 3 .22 0 .44-.03.65-.08l1.55 1.55c-.67.33-1.41.53-2.2.53-2.76 0-5-2.24-5-5 0-.79.2-1.53.53-2.2zm4.31-.78l3.15 3.15.02-.16c0-1.66-1.34-3-3-3l-.17.01z";

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
        DependencyProperty.Register(nameof(InputBorderBrush), typeof(Brush), typeof(RevealPasswordBox), new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1))));

    public Brush InputBorderBrush
    {
        get => (Brush)GetValue(InputBorderBrushProperty);
        set => SetValue(InputBorderBrushProperty, value);
    }

    public static readonly DependencyProperty InputFocusedBorderBrushProperty =
        DependencyProperty.Register(nameof(InputFocusedBorderBrush), typeof(Brush), typeof(RevealPasswordBox), new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x1D, 0x4E, 0xD8))));

    public Brush InputFocusedBorderBrush
    {
        get => (Brush)GetValue(InputFocusedBorderBrushProperty);
        set => SetValue(InputFocusedBorderBrushProperty, value);
    }

    public static readonly DependencyProperty InputForegroundProperty =
        DependencyProperty.Register(nameof(InputForeground), typeof(Brush), typeof(RevealPasswordBox), new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A))));

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
            EyeIconPath.Fill = new SolidColorBrush(Color.FromRgb(0x02, 0x84, 0xC7)); // Active highlight
            EyeToggleButton.ToolTip = "Ẩn mật khẩu";
        }
        else
        {
            EyeIconPath.Data = Geometry.Parse(IconDataEye);
            EyeIconPath.Fill = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)); // Neutral muted
            EyeToggleButton.ToolTip = "Hiện mật khẩu";
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
