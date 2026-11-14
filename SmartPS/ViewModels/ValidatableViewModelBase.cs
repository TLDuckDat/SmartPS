using System.Collections;
using System.ComponentModel;

namespace SmartPS.ViewModels;

/// <summary>
/// Nền chung cho ViewModel có kiểm tra dữ liệu nhập tại ô (INotifyDataErrorInfo).
/// Dùng cùng <c>ValidationErrorTemplate</c> trong Resources/Inputs.xaml: ô lỗi viền đỏ, hiện thông báo ngay tại ô,
/// nút Lưu khoá theo <see cref="HasErrors"/>.
/// </summary>
public abstract class ValidatableViewModelBase : ViewModelBase, INotifyDataErrorInfo
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public bool HasErrors => _errors.Count > 0;

    public IEnumerable GetErrors(string? propertyName)
    {
        if (propertyName is null)
        {
            return _errors.Values.SelectMany(e => e).ToList();
        }

        return _errors.TryGetValue(propertyName, out var list) ? list.ToList() : Array.Empty<string>();
    }

    /// <summary>Thay toàn bộ lỗi của thuộc tính; danh sách rỗng nghĩa là xoá lỗi.</summary>
    protected void SetErrors(string propertyName, IEnumerable<string> errors)
    {
        ArgumentException.ThrowIfNullOrEmpty(propertyName);
        var list = errors.Where(e => !string.IsNullOrWhiteSpace(e)).ToList();
        if (list.Count == 0)
        {
            ClearErrors(propertyName);
            return;
        }

        var hadErrors = HasErrors;
        _errors[propertyName] = list;
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
        if (hadErrors != HasErrors)
        {
            OnPropertyChanged(nameof(HasErrors));
        }
    }

    protected void ClearErrors(string propertyName)
    {
        if (!_errors.Remove(propertyName))
        {
            return;
        }

        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
        if (!HasErrors)
        {
            OnPropertyChanged(nameof(HasErrors));
        }
    }

    /// <summary>Đặt lỗi của thuộc tính theo kết quả kiểm tra: <paramref name="error"/> null/rỗng thì xoá lỗi.</summary>
    protected void ValidateProperty(string propertyName, string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            ClearErrors(propertyName);
        }
        else
        {
            SetErrors(propertyName, new[] { error });
        }
    }
}
