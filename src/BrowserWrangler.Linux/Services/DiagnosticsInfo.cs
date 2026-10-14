using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using BrowserWrangler.Core;
using BrowserWrangler.Core.Configuration;
using BrowserWrangler.Core.Logging;
using BrowserWrangler.Core.Platform.Linux;

namespace BrowserWrangler.Linux.Services;

/// <summary>A single labelled fact about this installation, shown on the About page.</summary>
internal sealed record DiagnosticsEntry(string Label, string Value, string? FolderPath = null);

/// <summary>
/// Collects the environment facts that make a bug report actionable. Deliberately
/// contains nothing user-identifying: no URLs, no rules, no browser profiles.
/// </summary>
internal static class DiagnosticsInfo
{
    private static readonly Lazy<IReadOnlyList<DiagnosticsEntry>> LazyEntries = new(BuildEntries);

    public static Version AppVersion { get; } =
        typeof(DiagnosticsInfo).Assembly.GetName().Version ?? new Version(1, 0, 0, 0);

    public static string VersionDisplay { get; } = AppVersion.ToString(3)
        + (AppInfo.IsDevChannel ? " (Dev Channel)" : string.Empty);

    public static string Copyright { get; } =
        typeof(DiagnosticsInfo).Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright
        ?? "\u00a9 Damian Hickey";

    public static string ConfigFilePath { get; } = new ConfigStore().ConfigFilePath;

    public static string RuleHitLogPath { get; } = new RuleHitLogStore().LogFilePath;

    public static IReadOnlyList<DiagnosticsEntry> Entries => LazyEntries.Value;

    public static string ToPlainText()
    {
        var builder = new StringBuilder();
        int width = Entries.Max(entry => entry.Label.Length);
        foreach (DiagnosticsEntry entry in Entries)
        {
            builder.Append(entry.Label.PadRight(width)).Append("  ").AppendLine(entry.Value);
        }

        return builder.ToString().TrimEnd();
    }

    private static IReadOnlyList<DiagnosticsEntry> BuildEntries()
    {
        var entries = new List<DiagnosticsEntry>
        {
            new("Version", VersionDisplay),
            new("Runtime", RuntimeInformation.FrameworkDescription),
            new("Avalonia", AvaloniaVersion()),
            new("OS", $"{OsName()} ({RuntimeInformation.OSArchitecture})"),
            new("Desktop", Describe(Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP"))),
            new("Session", LinuxKeyboard.IsX11Session ? "X11" : Describe(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"))),
            new("Packaging", Packaging()),
        };

        if (RuntimeInformation.ProcessArchitecture != RuntimeInformation.OSArchitecture)
        {
            entries.Add(new DiagnosticsEntry("Process", RuntimeInformation.ProcessArchitecture.ToString()));
        }

        entries.Add(new DiagnosticsEntry("Config file", DescribeFile(ConfigFilePath), Path.GetDirectoryName(ConfigFilePath)));
        entries.Add(new DiagnosticsEntry("Rule-hit log", DescribeFile(RuleHitLogPath), Path.GetDirectoryName(RuleHitLogPath)));
        return entries;
    }

    private static string Describe(string? value) => string.IsNullOrWhiteSpace(value) ? "unknown" : value;

    private static string Packaging()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPIMAGE")))
        {
            return "AppImage";
        }

        string exe = Environment.ProcessPath ?? string.Empty;
        return exe.StartsWith("/nix/store/", StringComparison.Ordinal) ? "Nix" : "Tarball";
    }

    private static string OsName()
    {
        try
        {
            foreach (string line in File.ReadLines("/etc/os-release"))
            {
                if (line.StartsWith("PRETTY_NAME=", StringComparison.Ordinal))
                {
                    return line["PRETTY_NAME=".Length..].Trim('"');
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return RuntimeInformation.OSDescription;
    }

    private static string DescribeFile(string path)
    {
        // Redact the home folder because this value is copied into prefilled bug report URLs.
        string home = Path.TrimEndingDirectorySeparator(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        string display = home.Length > 0 && path.StartsWith(home + "/", StringComparison.Ordinal)
            ? "~" + path[home.Length..]
            : path;
        return File.Exists(path) ? display : $"{display} (not created yet)";
    }

    private static string AvaloniaVersion() =>
        typeof(DiagnosticsInfo).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "AvaloniaVersion")?.Value
        ?? "unknown";
}
