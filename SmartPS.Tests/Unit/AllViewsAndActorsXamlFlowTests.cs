using System.Collections;
using System.IO;
using QuestPDF.Fluent;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.Navigation;
using SmartPS.Services.Audit;
using SmartPS.Services.Auth;
using SmartPS.Services.Authorization;
using SmartPS.Services.Dialog;
using SmartPS.Services.Localization;
using SmartPS.ViewModels;
using SmartPS.ViewModels.Auth;
using SmartPS.ViewModels.Dashboard;
using SmartPS.Views.Audit;
using SmartPS.Views.Auth;
using SmartPS.Views.Common;
using SmartPS.Views.Customers;
using SmartPS.Views.Dashboard;
using SmartPS.Views.GateControl;
using SmartPS.Views.Incidents;
using SmartPS.Views.Overview;
using SmartPS.Views.ParkingMap;
using SmartPS.Views.Pricing;
using SmartPS.Views.Reports;
using SmartPS.Views.RolePermissions;
using SmartPS.Views.Settings;
using SmartPS.Views.Shifts;
using SmartPS.Views.Transactions;
using SmartPS.Views.UserManagement;

namespace SmartPS.Tests.Unit;

/// <summary>
/// Kiểm thử toàn diện trên luồng STA thật của WPF:
/// 1. Kiểm tra không có bất kỳ StaticResource nào bị lỗi thứ tự khai báo (MS.Internal.NamedObject) trong toàn bộ ResourceDictionaries.
/// 2. Khởi tạo, Measure, Arrange, UpdateLayout và kết xuất ảnh (RenderTargetBitmap) tất cả các View, Dialog, và luồng Đăng nhập -> Dashboard cho từng Actor (Admin, Manager, Operator/Staff).
/// </summary>
public class AllViewsAndActorsXamlFlowTests
{
    private static readonly object StaLock = new();
    private static System.Windows.Threading.Dispatcher? _staDispatcher;

    private sealed class StubTabViewModel : ViewModelBase
    {
        public StubTabViewModel(NavigationItemType type) => Type = type;
        public NavigationItemType Type { get; }
    }

    private static void RunOnSta(Action action)
    {
        lock (StaLock)
        {
            if (_staDispatcher == null)
            {
                using var ready = new ManualResetEventSlim(false);
                var thread = new Thread(() =>
                {
                    _staDispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                    EnsureApplicationResources();
                    ready.Set();
                    System.Windows.Threading.Dispatcher.Run();
                })
                {
                    IsBackground = true,
                    Name = "SmartPS-Test-STA"
                };
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                ready.Wait();
            }

            Exception? captured = null;
            _staDispatcher!.Invoke(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    captured = ex;
                }
            });

            if (captured != null)
            {
                ExceptionDispatchInfo.Capture(captured).Throw();
            }
        }
    }

    private static void EnsureApplicationResources()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        if (app.Resources.MergedDictionaries.Count == 0)
        {
            string[] uris =
            [
                "pack://application:,,,/SmartPS;component/Resources/Colors.xaml",
                "pack://application:,,,/SmartPS;component/Resources/Icons.xaml",
                "pack://application:,,,/SmartPS;component/Resources/Converters.xaml",
                "pack://application:,,,/SmartPS;component/Resources/Inputs.xaml",
                "pack://application:,,,/SmartPS;component/Resources/Buttons.xaml",
                "pack://application:,,,/SmartPS;component/Resources/DataGrids.xaml",
                "pack://application:,,,/SmartPS;component/Resources/Languages/Strings.vi-VN.xaml"
            ];

            foreach (var uri in uris)
            {
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(uri, UriKind.Absolute) });
            }
        }
    }

    private static void AssertNotNamedObject(object? value, string context)
    {
        if (value == null) return;
        var typeName = value.GetType().FullName ?? string.Empty;
        Assert.False(
            typeName.Contains("NamedObject", StringComparison.Ordinal),
            $"Phát hiện StaticResource chưa được phân giải (MS.Internal.NamedObject) tại: {context}");
    }

    private static void VerifyStyleSettersAndTriggers(Style style, string styleKey)
    {
        foreach (var setterBase in style.Setters)
        {
            if (setterBase is Setter setter)
            {
                AssertNotNamedObject(setter.Value, $"Style '{styleKey}' -> Setter '{setter.Property?.Name}'");
            }
        }

        foreach (var triggerBase in style.Triggers)
        {
            if (triggerBase is Trigger trigger)
            {
                AssertNotNamedObject(trigger.Value, $"Style '{styleKey}' -> Trigger '{trigger.Property?.Name}' Value");
                foreach (var s in trigger.Setters.OfType<Setter>())
                {
                    AssertNotNamedObject(s.Value, $"Style '{styleKey}' -> Trigger '{trigger.Property?.Name}' -> Setter '{s.Property?.Name}'");
                }
            }
            else if (triggerBase is DataTrigger dataTrigger)
            {
                foreach (var s in dataTrigger.Setters.OfType<Setter>())
                {
                    AssertNotNamedObject(s.Value, $"Style '{styleKey}' -> DataTrigger '{dataTrigger.Value}' -> Setter '{s.Property?.Name}'");
                }
            }
            else if (triggerBase is MultiDataTrigger multiTrigger)
            {
                foreach (var s in multiTrigger.Setters.OfType<Setter>())
                {
                    AssertNotNamedObject(s.Value, $"Style '{styleKey}' -> MultiDataTrigger -> Setter '{s.Property?.Name}'");
                }
            }
        }
    }

    private static void RenderElementToBitmap(FrameworkElement element, double width, double height, string? saveName = null)
    {
        element.Width = width;
        element.Height = height;
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();

        var rtb = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(element);

        // Ảnh chụp chỉ ghi khi đặt SMARTPS_UI_SCREENSHOT_DIR (không ghi vào thư mục người dùng mặc định)
        var screenshotDir = Environment.GetEnvironmentVariable("SMARTPS_UI_SCREENSHOT_DIR");
        if (!string.IsNullOrWhiteSpace(saveName) && !string.IsNullOrWhiteSpace(screenshotDir))
        {
            var dir = screenshotDir;
            Directory.CreateDirectory(dir);
            var filePath = Path.Combine(dir, saveName + ".png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(filePath);
            encoder.Save(fs);
        }
    }

    private static void RenderWindowContentToBitmap(Window window, double width, double height, string? saveName = null)
    {
        if (window.Content is FrameworkElement content)
        {
            RenderElementToBitmap(content, width, height, saveName);
        }
    }

    [Fact]
    public void All_ResourceDictionaries_And_Styles_Have_Zero_Unresolved_StaticResources()
    {
        RunOnSta(() =>
        {
            foreach (var dict in Application.Current.Resources.MergedDictionaries)
            {
                foreach (DictionaryEntry entry in dict)
                {
                    var key = entry.Key?.ToString() ?? "<null>";
                    AssertNotNamedObject(entry.Value, $"Resource '{key}' trong '{dict.Source}'");

                    if (entry.Value is Style style)
                    {
                        VerifyStyleSettersAndTriggers(style, key);
                    }
                    else if (entry.Value is DrawingImage drawingImage)
                    {
                        Assert.NotNull(drawingImage.Drawing);
                        Assert.True(drawingImage.Width > 0 && drawingImage.Height > 0, $"DrawingImage '{key}' phải có kích thước hợp lệ.");
                    }
                }
            }
        });
    }

    [Fact]
    public void All_Module_Views_And_Dialogs_Instantiate_Layout_And_Render_Without_Exceptions()
    {
        RunOnSta(() =>
        {
            // 1. Kiểm tra 13 UserControl phân hệ chính
            (string Name, UserControl Control)[] moduleViews =
            [
                ("view_01_overview", new OverviewView()),
                ("view_02_gatecontrol", new GateControlView()),
                ("view_03_parkingmap", new ParkingMapView()),
                ("view_04_customers", new CustomersView()),
                ("view_05_incidents", new IncidentsView()),
                ("view_06_reports", new ReportsView()),
                ("view_07_transactions", new TransactionsView()),
                ("view_08_shifts", new ShiftsView()),
                ("view_09_users", new UserManagementView()),
                ("view_10_pricing", new PricingView()),
                ("view_11_settings", new SettingsView()),
                ("view_12_roles", new RolePermissionsView()),
                ("view_13_audit", new AuditLogView())
            ];

            foreach (var (name, control) in moduleViews)
            {
                RenderElementToBitmap(control, 1360, 860, name);
            }

            // 2. Kiểm tra NotificationDialog ở các chế độ khác nhau
            var notifInfo = new NotificationDialog("Hệ thống đã lưu dữ liệu thành công.", "Thông báo", NotificationType.Information);
            RenderWindowContentToBitmap(notifInfo, 440, 200, "dlg_notification_info");

            var notifConfirm = new NotificationDialog("Bạn có chắc chắn muốn đóng ca trực hiện tại?", "Xác nhận đóng ca", NotificationType.Question, NotificationButtons.YesNo);
            RenderWindowContentToBitmap(notifConfirm, 440, 210, "dlg_notification_confirm");

            RenderWindowContentToBitmap(new HouseholdDialog(), 380, 280);
            RenderWindowContentToBitmap(new AddBlacklistDialog(new List<SmartPS.Models.Parking.Customer>()), 440, 360);
        });
    }

    [Fact]
    public void Login_And_Dashboard_Flow_Works_For_All_Actors_Admin_Manager_Operator()
    {
        RunOnSta(() =>
        {
            var context = new CurrentUserContext();
            var audit = new FakeAuditService();
            var dialog = new FakeDialogService();
            var auth = new FakeAuthService(context);
            var loc = new FakeLocalizationService();
            var creds = new InMemoryCredentialStore();

            var services = new ServiceCollection();
            services.AddSingleton<ICurrentUserContext>(context);
            services.AddSingleton<IPermissionService, PermissionService>();
            services.AddSingleton<IAuthService>(auth);
            services.AddSingleton<IDialogService>(dialog);
            services.AddSingleton<ILocalizationService>(loc);
            services.AddSingleton<IAuditService>(audit);
            services.AddSingleton<ICredentialStore>(creds);
            var provider = services.BuildServiceProvider();

            // 1. Kiểm tra LoginView (trạng thái kết nối DB thành công & mất kết nối DB)
            var loginVm = ActivatorUtilities.CreateInstance<LoginViewModel>(provider);
            loginVm.SetInitialDbStatus(true);
            var loginView = new LoginView(loginVm);
            RenderWindowContentToBitmap(loginView, 960, 620, "flow_01_login_connected");

            loginVm.SetInitialDbStatus(false);
            RenderWindowContentToBitmap(loginView, 960, 620, "flow_02_login_disconnected");

            // 2. Kiểm tra DashboardView với từng Actor: Admin (13 phân hệ), Manager (12 phân hệ), Operator/Staff (7 phân hệ)
            var actors = new (string ActorKey, SmartPS.Models.Auth.User User, int ExpectedVisibleModules)[]
            {
                ("admin", TestUsers.AdminWithoutGrants(), 13),
                ("manager", TestUsers.Manager(), 12),
                ("staff", TestUsers.Operator(), 7)
            };

            foreach (var (actorKey, user, expectedVisible) in actors)
            {
                context.SetUser(null);
                var dashVm = ActivatorUtilities.CreateInstance<DashboardViewModel>(provider);
                dashVm.ViewModelFactory = type => new StubTabViewModel(type);
                context.SetUser(user);

                var visibleItems = dashVm.NavItems.Where(i => i.IsVisible).ToList();
                Assert.Equal(expectedVisible, visibleItems.Count);

                var dashWindow = new DashboardView(dashVm);

                // Đóng tất cả các thẻ trước để kiểm tra trạng thái chưa mở thẻ nào (hiển thị mascot Capybara)
                dashVm.CloseAllTabsCommand.Execute(null);
                Assert.Empty(dashVm.OpenTabs);
                RenderWindowContentToBitmap(dashWindow, 1360, 840, $"flow_03_dashboard_{actorKey}_empty");

                // Mở tất cả các phân hệ mà Actor có quyền và kiểm tra từng thẻ Tab (kích hoạt TabModuleIllustrationStyle & NavModuleIllustrationStyle)
                foreach (var item in visibleItems)
                {
                    dashVm.NavigateCommand.Execute(item);
                    Assert.Equal(item.Id, dashVm.SelectedTab!.Item.Id);
                }

                Assert.Equal(visibleItems.Count, dashVm.OpenTabs.Count);
                RenderWindowContentToBitmap(dashWindow, 1360, 840, $"flow_04_dashboard_{actorKey}_tabs_open");

                // Đóng tất cả các thẻ
                dashVm.CloseAllTabsCommand.Execute(null);
                Assert.Empty(dashVm.OpenTabs);
            }
        });
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in FindVisualChildren<T>(child)) yield return nested;
        }
    }

    [Fact]
    public void ComboBox_selection_box_shows_DisplayMemberPath_text_with_modern_style()
    {
        RunOnSta(() =>
        {
            var style = (Style)Application.Current.FindResource("ModernComboBoxStyle");
            var combo = new ComboBox
            {
                Style = style,
                Width = 220,
                DisplayMemberPath = "TypeName",
                ItemsSource = new List<SmartPS.Models.Parking.VehicleType>
                {
                    new() { VehicleTypeId = 1, TypeName = "Xe máy" }
                },
                SelectedIndex = 0
            };
            var window = new Window { Content = combo, Width = 300, Height = 100 };
            window.Show();
            try
            {
                combo.ApplyTemplate();
                combo.UpdateLayout();
                var site = (ContentPresenter)combo.Template.FindName("ContentSite", combo);
                site.UpdateLayout();

                var texts = FindVisualChildren<TextBlock>(site).Select(t => t.Text).ToList();
                Assert.DoesNotContain("SmartPS.Models.Parking.VehicleType", texts);
                Assert.Contains("Xe máy", texts);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ComboBox_with_XAML_ComboBoxItems_shows_their_content_text()
    {
        RunOnSta(() =>
        {
            var style = (Style)Application.Current.FindResource("ModernComboBoxStyle");
            var combo = new ComboBox { Style = style, Width = 200 };
            combo.Items.Add(new ComboBoxItem { Content = "Tất cả" });
            combo.Items.Add(new ComboBoxItem { Content = "Tiền mặt" });
            combo.SelectedIndex = 0;

            var window = new Window { Content = combo, Width = 300, Height = 100 };
            window.Show();
            try
            {
                combo.ApplyTemplate();
                combo.UpdateLayout();
                var site = (ContentPresenter)combo.Template.FindName("ContentSite", combo);
                site.UpdateLayout();

                var texts = FindVisualChildren<TextBlock>(site).Select(t => t.Text).ToList();
                Assert.Contains("Tất cả", texts);
                Assert.DoesNotContain(texts, t => t.Contains("ComboBoxItem", StringComparison.Ordinal));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Report_preview_window_constructs_and_renders_without_exceptions()
    {
        RunOnSta(() =>
        {
            var document = QuestPDF.Fluent.Document.Create(container =>
            {
                container.Page(page => page.Content().Text("SmartPS report smoke test"));
            });
            // QuestPDF Community được cấu hình như trong ReportPdfWriter
            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

            var preview = new SmartPS.Views.Reports.ReportPreviewWindow(document, "BaoCao_SmartPS_test.xlsx");
            preview.Show();
            try
            {
                preview.UpdateLayout();
                AssertNotNamedObject(preview, "ReportPreviewWindow");
            }
            finally
            {
                preview.Close();
            }
        });
    }

    [Fact]
    public void Report_pdf_document_with_sample_data_renders_pages()
    {
        RunOnSta(() =>
        {
            var labels = SmartPS.Services.Reports.ReportWorkbookLabels.Resolve(new FakeLocalizationService());
            var writer = new SmartPS.Services.Reports.ReportPdfWriter(labels);
            var document = writer.CreateDocument(ReportResults.Sample(), ReportResults.Sessions());

            var images = document.GenerateImages().ToList();
            Assert.NotEmpty(images);
        });
    }

    private sealed class InMemoryCredentialStore : ICredentialStore
    {
        public SavedCredentials? Load() => null;
        public void Save(string username, string password) { }
        public void Clear() { }
    }
}
