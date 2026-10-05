using System.Windows.Input;

namespace SmartPS.ViewModels;

public class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    private readonly Predicate<T?>? _canExecute;

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public RelayCommand(Action<T?> execute, Predicate<T?>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter)
    {
        if (parameter is null && typeof(T).IsValueType)
            return _canExecute?.Invoke(default) ?? true;

        if (parameter is null || parameter is T)
            return _canExecute?.Invoke((T?)parameter) ?? true;

        return false;
    }

    public void Execute(object? parameter)
    {
        if (parameter is null && typeof(T).IsValueType)
        {
            _execute(default);
            return;
        }

        _execute((T?)parameter);
    }

    public void RaiseCanExecuteChanged()
    {
        CommandManager.InvalidateRequerySuggested();
    }
}
