using BrowserWrangler.Core.Configuration;
using BrowserWrangler.Core.Discovery;

namespace BrowserWrangler.Services;

/// <summary>
/// Shared app state for the config UI: the loaded config plus save/refresh helpers.
/// </summary>
public static class AppState
{
    private static readonly ConfigStore Store = new();
    private static readonly UpdateAutomationService UpdateService = new();
    private static readonly object ConfigLock = new();
    private static bool _updateAutomationStarted;

    public static AppConfig Config { get; private set; } = LaunchContext.Config;

    public static UpdateAutomationService Updates => UpdateService;

    public static void Save()
    {
        lock (ConfigLock)
        {
            Store.Save(Config);
        }
    }

    public static T ReadConfig<T>(Func<AppConfig, T> reader)
    {
        lock (ConfigLock)
        {
            return reader(Config);
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

    public static void EnsureUpdateAutomationStarted()
    {
        if (_updateAutomationStarted)
        {
            return;
        }

        _updateAutomationStarted = true;
        UpdateService.Start();
    }

    public static void StopUpdateAutomation()
    {
        if (!_updateAutomationStarted)
        {
            return;
        }

        _updateAutomationStarted = false;
        UpdateService.Stop();
    }

    /// <summary>Re-discovers browsers and merges with the saved set, preserving user data.</summary>
    public static void RefreshBrowsers()
    {
        var discovered = BrowserDiscovery.DiscoverBrowsers(Core.AppInfo.ProgId);
        MutateAndSave(config => config.Browsers = BrowserMerger.Merge(discovered, config.Browsers));
    }
}
