using System.Windows;
using System.Windows.Input;
using SmartPS.Services.Localization;
using SmartPS.ViewModels.Dashboard;

namespace SmartPS.Views.Help;

/// <summary>Bảng phím tắt: mỗi phím tắt một dòng riêng, chia theo nhóm cho dễ tra.</summary>
public partial class ShortcutsWindow : Window
{
    public sealed record ShortcutItem(string Keys, string Description);

    public sealed record ShortcutGroup(string Title, IReadOnlyList<ShortcutItem> Items);

    private readonly DashboardViewModel _viewModel;

    public ShortcutsWindow(DashboardViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        GroupsList.ItemsSource = BuildGroups(viewModel);
    }

    /// <summary>Danh sách phím tắt; phần "Mở nhanh" chỉ gồm các chức năng người dùng hiện tại được phép mở.</summary>
    public static IReadOnlyList<ShortcutGroup> BuildGroups(DashboardViewModel viewModel)
    {
        var modules = viewModel.NavItems
            .Where(i => i.ShortcutDigit.HasValue && i.IsVisible)
            .OrderBy(i => i.ShortcutDigit)
            .Select(i => new ShortcutItem(i.ShortcutText, AppText.Get("Str_Shortcuts_OpenModule", i.Title)))
            .ToList();

        var groups = new List<ShortcutGroup>
        {
            new(AppText.Get("Str_Shortcuts_GroupGeneral"), new List<ShortcutItem>
            {
                new("F1", AppText.Get("Str_Shortcuts_UserGuide")),
                new("F2", AppText.Get("Str_Shortcuts_ShowShortcuts")),
                new("F5", AppText.Get("Str_Shortcuts_Refresh")),
                new("Ctrl+I", AppText.Get("Str_Shortcuts_Profile")),
                new("Ctrl+L", AppText.Get("Str_Shortcuts_Logout")),
                new("Alt+F4", AppText.Get("Str_Shortcuts_Exit")),
            }),
        };

        if (modules.Count > 0)
        {
            groups.Add(new ShortcutGroup(AppText.Get("Str_Shortcuts_GroupModules"), modules));
        }

        groups.Add(new ShortcutGroup(AppText.Get("Str_Shortcuts_GroupTabs"), new List<ShortcutItem>
        {
            new("Ctrl+W", AppText.Get("Str_Shortcuts_CloseTab")),
            new("Ctrl+F4", AppText.Get("Str_Shortcuts_CloseTab")),
            new("Ctrl+Shift+W", AppText.Get("Str_Shortcuts_CloseAllTabs")),
            new(AppText.Get("Str_Shortcuts_MiddleClickKey"), AppText.Get("Str_Shortcuts_MiddleClick")),
        }));

        groups.Add(new ShortcutGroup(AppText.Get("Str_Shortcuts_GroupMenus"), new List<ShortcutItem>
        {
            new("Alt+H", AppText.Get("Str_Shortcuts_MenuSystem")),
            new("Alt+N", AppText.Get("Str_Shortcuts_MenuOperations")),
            new("Alt+D", AppText.Get("Str_Shortcuts_MenuCatalog")),
            new("Alt+B", AppText.Get("Str_Shortcuts_MenuReports")),
            new("Alt+Q", AppText.Get("Str_Shortcuts_MenuAdmin")),
            new("Alt+G", AppText.Get("Str_Shortcuts_MenuHelp")),
        }));

        groups.Add(new ShortcutGroup(AppText.Get("Str_Shortcuts_GroupForms"), new List<ShortcutItem>
        {
            new("Enter", AppText.Get("Str_Shortcuts_Enter")),
            new("Esc", AppText.Get("Str_Shortcuts_Escape")),
            new("Tab", AppText.Get("Str_Shortcuts_Tab")),
        }));

        return groups;
    }

    private void OnOpenGuideClick(object sender, RoutedEventArgs e)
    {
        _viewModel.OpenUserGuideCommand.Execute(null);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnCloseExecuted(object sender, ExecutedRoutedEventArgs e) => Close();
}
