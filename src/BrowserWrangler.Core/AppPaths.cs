namespace BrowserWrangler.Core;

/// <summary>
/// Per-user storage locations. Windows keeps everything under %LOCALAPPDATA%; other platforms
/// follow the XDG base directory spec (config, state and cache are separate there).
/// </summary>
public static class AppPaths
{
    public static string ConfigDirectory { get; } = Resolve("XDG_CONFIG_HOME", ".config");

    public static string StateDirectory { get; } = Resolve("XDG_STATE_HOME", Path.Combine(".local", "state"));

    public static string CacheDirectory { get; } = Resolve("XDG_CACHE_HOME", ".cache");

    public static string ConfigFilePath => Path.Combine(ConfigDirectory, "config.json");

    public static string RuleHitLogFilePath => Path.Combine(StateDirectory, "rule-hits.jsonl");

    public static string UpdatesDirectory => Path.Combine(CacheDirectory, "updates");

    private static string Resolve(string xdgVariable, string homeRelativeFallback)
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppInfo.LocalDataDirectoryName);
        }

        return Path.Combine(XdgBaseDirectory(xdgVariable, homeRelativeFallback), AppInfo.LinuxAppId);
    }

    /// <summary>Returns an XDG base directory, ignoring relative values as the spec requires.</summary>
    public static string XdgBaseDirectory(string variable, string homeRelativeFallback)
    {
        string? value = Environment.GetEnvironmentVariable(variable);
        if (!string.IsNullOrEmpty(value) && Path.IsPathRooted(value))
        {
            return value;
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), homeRelativeFallback);
    }
}
