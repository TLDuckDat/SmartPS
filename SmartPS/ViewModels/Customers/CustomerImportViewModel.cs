// Điều phối chọn file, xem trước, lưu import và hiển thị tiến độ cho hộp thoại import.

using System.Collections.ObjectModel;
using SmartPS.DTOs.Customers;
using SmartPS.Services.Customers;
using SmartPS.Services.Dialog;
using SmartPS.Services.Localization;

namespace SmartPS.ViewModels.Customers;

public sealed class CustomerImportViewModel : ViewModelBase, IDisposable
{
    private readonly ICustomerImportService _importService;
    private readonly IDialogService _dialogService;
    private CancellationTokenSource? _importCancellation;
    private CustomerImportPreview? _preview;
    private string _filePath = string.Empty;
    private bool _isBusy;
    private int _progressPercent;
    private string _progressMessage = string.Empty;
    private int _processedRows;
    private int _totalRows;

    public CustomerImportViewModel(ICustomerImportService importService, IDialogService dialogService)
    {
        _importService = importService ?? throw new ArgumentNullException(nameof(importService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

        ChooseFileCommand = new RelayCommand(ChooseFile, () => !IsBusy);
        PreviewCommand = new AsyncRelayCommand(PreviewAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(FilePath));
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy && _preview?.CanSave == true);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
    }

    public event Action<bool>? RequestClose;

    public RelayCommand ChooseFileCommand { get; }
    public AsyncRelayCommand PreviewCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }

    public ObservableCollection<CustomerImportRowResult> Rows { get; } = new();

    public CustomerImportSaveResult? SaveResult { get; private set; }

    public string FilePath
    {
        get => _filePath;
        private set
        {
            if (SetProperty(ref _filePath, value))
            {
                ClearPreview();
                PreviewCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                ChooseFileCommand.RaiseCanExecuteChanged();
                PreviewCommand.RaiseCanExecuteChanged();
                SaveCommand.RaiseCanExecuteChanged();
                CancelCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(CanClose));
            }
        }
    }

    public bool CanClose => !IsBusy;

    public int ProgressPercent
    {
        get => _progressPercent;
        private set => SetProperty(ref _progressPercent, value);
    }

    public string ProgressMessage
    {
        get => _progressMessage;
        private set => SetProperty(ref _progressMessage, value);
    }

    public int ProcessedRows
    {
        get => _processedRows;
        private set => SetProperty(ref _processedRows, value);
    }

    public int TotalRows
    {
        get => _totalRows;
        private set => SetProperty(ref _totalRows, value);
    }

    public int ValidRows => _preview?.ValidRows ?? 0;
    public int WarningRows => _preview?.WarningRows ?? 0;
    public int ErrorRows => _preview?.ErrorRows ?? 0;
    public bool HasPreview => _preview != null;
    public bool CanSave => _preview?.CanSave == true && !IsBusy;

    private void ChooseFile()
    {
        var path = _dialogService.ShowOpenFileDialog(
            "Excel/CSV (*.xlsx;*.csv)|*.xlsx;*.csv|Excel (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv",
            AppText.Get("Str_Customers_ImportTitle"));
        if (!string.IsNullOrWhiteSpace(path))
        {
            FilePath = path;
        }
    }

    private async Task PreviewAsync()
    {
        ClearPreview();
        await RunAsync(async cancellationToken =>
        {
            var progress = new Progress<CustomerImportProgress>(ApplyProgress);
            _preview = await _importService.PreviewAsync(FilePath, progress, cancellationToken);
            Rows.Clear();
            foreach (var row in _preview.Rows)
            {
                Rows.Add(row);
            }

            OnPropertyChanged(nameof(ValidRows));
            OnPropertyChanged(nameof(WarningRows));
            OnPropertyChanged(nameof(ErrorRows));
            OnPropertyChanged(nameof(HasPreview));
            OnPropertyChanged(nameof(CanSave));
            SaveCommand.RaiseCanExecuteChanged();
        });
    }

    private async Task SaveAsync()
    {
        await RunAsync(async cancellationToken =>
        {
            if (_preview == null)
            {
                return;
            }

            var progress = new Progress<CustomerImportProgress>(ApplyProgress);
            SaveResult = await _importService.SaveAsync(_preview, progress, cancellationToken);
            RequestClose?.Invoke(true);
        });
    }

    private async Task RunAsync(Func<CancellationToken, Task> operation)
    {
        if (IsBusy)
        {
            return;
        }

        _importCancellation?.Dispose();
        _importCancellation = new CancellationTokenSource();
        IsBusy = true;
        ProgressPercent = 0;
        ProcessedRows = 0;
        TotalRows = 0;
        ProgressMessage = AppText.Get("Str_Customers_ImportStarting");

        try
        {
            await operation(_importCancellation.Token);
            if (ProgressPercent < 100 && _preview != null)
            {
                ProgressPercent = 100;
            }
        }
        catch (OperationCanceledException) when (_importCancellation.IsCancellationRequested)
        {
            ProgressMessage = AppText.Get("Msg_Customers_ImportCanceled");
        }
        catch (Exception ex)
        {
            _dialogService.ShowError(AppText.Get("Msg_Customers_ImportFailed", ex.Message));
        }
        finally
        {
            IsBusy = false;
            _importCancellation.Dispose();
            _importCancellation = null;
        }
    }

    private void ApplyProgress(CustomerImportProgress progress)
    {
        ProgressPercent = progress.Percent;
        ProcessedRows = progress.ProcessedRows;
        TotalRows = progress.TotalRows;
        ProgressMessage = progress.Phase;
    }

    private void Cancel()
    {
        _importCancellation?.Cancel();
    }

    private void ClearPreview()
    {
        _preview = null;
        SaveResult = null;
        Rows.Clear();
        ProgressPercent = 0;
        ProcessedRows = 0;
        TotalRows = 0;
        ProgressMessage = string.Empty;
        OnPropertyChanged(nameof(ValidRows));
        OnPropertyChanged(nameof(WarningRows));
        OnPropertyChanged(nameof(ErrorRows));
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.RaiseCanExecuteChanged();
    }

    public void Close()
    {
        if (IsBusy)
        {
            Cancel();
            return;
        }

        RequestClose?.Invoke(false);
    }

    public void Dispose()
    {
        _importCancellation?.Cancel();
        _importCancellation?.Dispose();
        _importCancellation = null;
    }
}
