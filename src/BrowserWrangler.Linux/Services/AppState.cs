using System.Diagnostics;
using BrowserWrangler.Core.Configuration;
using BrowserWrangler.Core.Discovery;
using BrowserWrangler.Core.Platform;

namespace BrowserWrangler.Linux.Services;

/// <summary>Shared app state for the config UI: the loaded config plus save/refresh helpers.</summary>
internal static class AppState
{
    private static readonly ConfigStore Store = new();
    private static readonly object ConfigLock = new();

    public static AppConfig Config { get; set; } = new();

    public static void Save()
    {
        lock (ConfigLock)
        {
            Store.Save(Config);
        }
    }

    public static void MutateAndSave(Action<AppConfig> mutator)
    {
        lock (ConfigLock)
        {
            mutator(Config);
            Store.Save(Config);
        }
    }

    /// <summary>Rediscovers installed browsers and merges them with the saved ones.</summary>
    public static void RefreshBrowsers()
    {
        List<Core.Models.Browser> discovered = PlatformServices.Current.DiscoverBrowsers();
        MutateAndSave(c => c.Browsers = BrowserMerger.Merge(discovered, c.Browsers));
    }

    public static void TryRefreshBrowsers()
    {
        try
        {
            RefreshBrowsers();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or Microsoft.Data.Sqlite.SqliteException)
        {
            Trace.TraceWarning($"Browser refresh failed: {ex}");
        }
    }

    /// <summary>Opens a URL with the desktop's handler (never routes through ourselves).</summary>
    public static void OpenExternal(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo("xdg-open", [target]) { UseShellExecute = false })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Trace.TraceWarning($"xdg-open failed: {ex.Message}");
        }
    }
}
