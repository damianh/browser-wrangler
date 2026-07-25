using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using BrowserWrangler.Core.Configuration;
using BrowserWrangler.Core.Logging;

namespace BrowserWrangler.Services;

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

    public static string VersionDisplay { get; } = AppVersion.ToString(3);

    public static string Copyright { get; } =
        typeof(DiagnosticsInfo).Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright
        ?? "\u00a9 Damian Hickey";

    public static string ConfigFilePath { get; } = new ConfigStore().ConfigFilePath;

    public static string RuleHitLogPath { get; } = new RuleHitLogStore().LogFilePath;

    public static IReadOnlyList<DiagnosticsEntry> Entries => LazyEntries.Value;

    /// <summary>Renders the same facts as a plain-text block for the clipboard and issue reports.</summary>
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
            new("Windows App SDK", WindowsAppSdkVersion()),
            new("Windows", $"{Environment.OSVersion.Version} ({RuntimeInformation.OSArchitecture})"),
        };

        // Only worth showing when it differs, e.g. an x64 build running on an Arm64 machine.
        if (RuntimeInformation.ProcessArchitecture != RuntimeInformation.OSArchitecture)
        {
            entries.Add(new DiagnosticsEntry("Process", RuntimeInformation.ProcessArchitecture.ToString()));
        }

        entries.Add(new DiagnosticsEntry("Config file", DescribeFile(ConfigFilePath), Path.GetDirectoryName(ConfigFilePath)));
        entries.Add(new DiagnosticsEntry("Rule-hit log", DescribeFile(RuleHitLogPath), Path.GetDirectoryName(RuleHitLogPath)));
        return entries;
    }

    private static string DescribeFile(string path) => File.Exists(path) ? path : $"{path} (not created yet)";

    private static string WindowsAppSdkVersion()
    {
        try
        {
            string? stamped = typeof(DiagnosticsInfo).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => attribute.Key == "WindowsAppSdkVersion")?.Value;
            if (!string.IsNullOrWhiteSpace(stamped))
            {
                return stamped;
            }

            // Fall back to the WinUI assembly, which reports its own version rather than the SDK's.
            Assembly winUi = typeof(Microsoft.UI.Xaml.Application).Assembly;
            string location = winUi.Location;
            return string.IsNullOrEmpty(location)
                ? winUi.GetName().Version?.ToString() ?? "unknown"
                : FileVersionInfo.GetVersionInfo(location).FileVersion ?? "unknown";
        }
        catch (Exception)
        {
            return "unknown";
        }
    }
}
