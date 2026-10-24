using System.Runtime.CompilerServices;
using System.Xml.Linq;
using SmartPS.Services.Localization;

namespace SmartPS.Tests.TestSupport;

/// <summary>
/// Tests have no WPF Application, so LocalizationService never fills AppText. Load the Vietnamese dictionary from the
/// repository once, so services and models return the same Vietnamese texts as the app does by default.
/// </summary>
internal static class AppTextBootstrap
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [ModuleInitializer]
    internal static void LoadVietnamese()
    {
        var path = Path.Combine(RepoPaths.Languages, "Strings.vi-VN.xaml");
        var catalog = XDocument.Load(path).Root!.Elements()
            .Select(e => (Key: (string?)e.Attribute(X + "Key"), e.Value))
            .Where(e => e.Key is not null)
            .ToDictionary(e => e.Key!, e => e.Value, StringComparer.Ordinal);
        AppText.Load(catalog);
    }
}
