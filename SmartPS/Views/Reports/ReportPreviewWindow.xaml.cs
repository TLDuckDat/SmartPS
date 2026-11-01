using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SmartPS.Models.Reports;
using SmartPS.Services.Dialog;
using SmartPS.Services.Localization;
using SmartPS.Services.Reports;

namespace SmartPS.Views.Reports;

public partial class ReportPreviewWindow : Window
{
    private readonly IDocument _document;
    private readonly ReportFilter _filter;
    private readonly IReportExportService? _exportService;
    private readonly IDialogService? _dialogService;
    private readonly ILocalizationService? _localization;
    private readonly IFileDialogService? _fileDialogService;
    private readonly string _defaultFileName;

    public ReportPreviewWindow(
        IDocument document,
        ReportFilter filter,
        IReportExportService exportService,
        IDialogService dialogService,
        ILocalizationService localization,
        IFileDialogService fileDialogService)
    {
        InitializeComponent();
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _filter = filter ?? throw new ArgumentNullException(nameof(filter));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _fileDialogService = fileDialogService ?? throw new ArgumentNullException(nameof(fileDialogService));
        _defaultFileName = _exportService.GetDefaultFileName(_filter.Range);

        Loaded += OnLoaded;
    }

    public ReportPreviewWindow(IDocument document, string defaultFileName)
    {
        InitializeComponent();
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _filter = new ReportFilter(new ReportDateRange(DateOnly.FromDateTime(DateTime.Today), DateOnly.FromDateTime(DateTime.Today)));
        _defaultFileName = defaultFileName;

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        LoadingProgress.Visibility = Visibility.Visible;
        try
        {
            var images = await Task.Run(() => _document.GenerateImages().ToList());
            var fixedDoc = new FixedDocument();

            foreach (var imgBytes in images)
            {
                var bitmap = new BitmapImage();
                using (var ms = new MemoryStream(imgBytes))
                {
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = ms;
                    bitmap.EndInit();
                }
                bitmap.Freeze();

                var pageImage = new System.Windows.Controls.Image
                {
                    Source = bitmap,
                    Stretch = Stretch.Uniform,
                    Width = 842,
                    Height = 595
                };

                var fixedPage = new FixedPage
                {
                    Width = 842,
                    Height = 595,
                    Background = Brushes.White
                };
                fixedPage.Children.Add(pageImage);

                var pageContent = new PageContent();
                ((IAddChild)pageContent).AddChild(fixedPage);
                fixedDoc.Pages.Add(pageContent);
            }

            DocViewer.Document = fixedDoc;
            DocViewer.FitToWidth();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Không thể tạo bản xem trước: {ex.Message}", "Lỗi xem trước", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            LoadingProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void BtnPrint_Click(object sender, RoutedEventArgs e)
    {
        if (DocViewer.Document == null) return;

        var printDialog = new PrintDialog();
        if (printDialog.ShowDialog() == true)
        {
            printDialog.PrintDocument(DocViewer.Document.DocumentPaginator, "Báo cáo SmartPS");
        }
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var filterText = _localization?.GetString(ReportTextKeys.SaveDialogFilter)
            ?? "Excel Workbook (*.xlsx)|*.xlsx|CSV UTF-8 (*.csv)|*.csv|PDF Document (*.pdf)|*.pdf";
        var title = _localization?.GetString(ReportTextKeys.SaveDialogTitle) ?? "Lưu báo cáo";

        string? savePath = null;
        if (_fileDialogService != null)
        {
            savePath = _fileDialogService.ShowSaveFileDialog(filterText, _defaultFileName, title);
        }
        else
        {
            var sfd = new SaveFileDialog
            {
                Filter = filterText,
                FileName = _defaultFileName,
                Title = title,
                OverwritePrompt = true
            };
            if (sfd.ShowDialog(this) == true)
            {
                savePath = sfd.FileName;
            }
        }

        if (string.IsNullOrWhiteSpace(savePath))
        {
            return;
        }

        try
        {
            BtnSave.IsEnabled = false;

            if (_exportService != null)
            {
                var exportResult = await _exportService.ExportAsync(_filter, savePath);

                if (exportResult.Status == ReportExportStatus.Success)
                {
                    var targetPath = exportResult.FilePath ?? savePath;
                    var promptMsg = $"Đã lưu báo cáo thành công tại:\n{targetPath}\n\nBạn có muốn mở file vừa xuất lên luôn không?";

                    bool shouldOpen;
                    if (_dialogService != null)
                    {
                        shouldOpen = _dialogService.ShowYesNo(
                            promptMsg,
                            "Lưu báo cáo thành công",
                            "Mở file",
                            "Đóng");
                    }
                    else
                    {
                        var res = MessageBox.Show(this, promptMsg, "Lưu báo cáo thành công", MessageBoxButton.YesNo, MessageBoxImage.Information);
                        shouldOpen = res == MessageBoxResult.Yes;
                    }

                    if (shouldOpen)
                    {
                        TryOpenFile(targetPath);
                    }
                }
                else if (exportResult.Status == ReportExportStatus.FileLocked)
                {
                    ShowErrorDialog(_localization?.GetString(ReportTextKeys.MsgExportFileLocked) ?? "Tệp đang bị khóa bởi tiến trình khác.");
                }
                else
                {
                    ShowErrorDialog(_localization?.GetString(ReportTextKeys.MsgExportError) ?? "Không thể lưu báo cáo.");
                }
            }
            else
            {
                // Fallback nếu không có exportService: lưu trực tiếp PDF
                using (var fs = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    _document.GeneratePdf(fs);
                }

                var promptMsg = $"Đã lưu báo cáo thành công tại:\n{savePath}\n\nBạn có muốn mở file vừa xuất lên luôn không?";
                var res = MessageBox.Show(this, promptMsg, "Lưu báo cáo thành công", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (res == MessageBoxResult.Yes)
                {
                    TryOpenFile(savePath);
                }
            }
        }
        catch (Exception ex)
        {
            ShowErrorDialog($"Lỗi khi lưu báo cáo: {ex.Message}");
        }
        finally
        {
            BtnSave.IsEnabled = true;
        }
    }

    private void ShowErrorDialog(string message)
    {
        if (_dialogService != null)
        {
            _dialogService.ShowError(message);
        }
        else
        {
            MessageBox.Show(this, message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static void TryOpenFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open file: {ex.Message}");
        }
    }
}
