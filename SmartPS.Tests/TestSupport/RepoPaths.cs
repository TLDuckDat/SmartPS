namespace SmartPS.Tests.TestSupport;

/// <summary>
/// Locates source folders of the repository from the test output directory
/// (used by source-scanning tests and localization parity tests).
/// </summary>
public static class RepoPaths
{
    private static readonly Lazy<string> s_root = new(FindRoot);

    /// <summary>Repository root (folder that contains SmartPS.slnx).</summary>
    public static string Root => s_root.Value;

    /// <summary>The WPF application project folder (SmartPS/).</summary>
    public static string AppProject => Path.Combine(Root, "SmartPS");

    public static string Services => Path.Combine(AppProject, "Services");

    public static string ViewModels => Path.Combine(AppProject, "ViewModels");

    public static string Languages => Path.Combine(AppProject, "Resources", "Languages");

    public static IEnumerable<string> CSharpFiles(string folder) =>
        Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                     && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SmartPS.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate SmartPS.slnx above '{AppContext.BaseDirectory}'.");
    }
}
