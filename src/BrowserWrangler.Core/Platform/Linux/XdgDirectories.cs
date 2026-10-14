namespace BrowserWrangler.Core.Platform.Linux;

/// <summary>XDG base directories (https://specifications.freedesktop.org/basedir-spec/).</summary>
public sealed record XdgDirectories(
    string Home,
    string DataHome,
    IReadOnlyList<string> DataDirs,
    string ConfigHome,
    IReadOnlyList<string> ConfigDirs,
    IReadOnlyList<string> CurrentDesktops)
{
    public static XdgDirectories FromEnvironment()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new XdgDirectories(
            home,
            AppPaths.XdgBaseDirectory("XDG_DATA_HOME", Path.Combine(".local", "share")),
            SplitPaths(Environment.GetEnvironmentVariable("XDG_DATA_DIRS"), ["/usr/local/share", "/usr/share"]),
            AppPaths.XdgBaseDirectory("XDG_CONFIG_HOME", ".config"),
            SplitPaths(Environment.GetEnvironmentVariable("XDG_CONFIG_DIRS"), ["/etc/xdg"]),
            (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? string.Empty)
                .Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(d => d.ToLowerInvariant())
                .ToList());
    }

    /// <summary>All application directories, highest priority first.</summary>
    public IEnumerable<string> ApplicationDirs =>
        new[] { DataHome }.Concat(DataDirs).Select(d => Path.Combine(d, "applications"));

    public string UserApplicationsDir => Path.Combine(DataHome, "applications");

    private static List<string> SplitPaths(string? value, string[] fallback)
    {
        List<string> paths = (value ?? string.Empty)
            .Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(Path.IsPathRooted)
            .Distinct()
            .ToList();
        return paths.Count > 0 ? paths : [.. fallback];
    }
}
