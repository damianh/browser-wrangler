using BrowserWrangler.Core.Configuration;
using BrowserWrangler.Core.Discovery;

namespace BrowserWrangler.App.Services;

/// <summary>
/// Shared app state for the config UI: the loaded config plus save/refresh helpers.
/// </summary>
public static class AppState
{
    private static readonly ConfigStore Store = new();

    public static AppConfig Config { get; private set; } = LaunchContext.Config;

    public static void Save() => Store.Save(Config);

    /// <summary>Re-discovers browsers and merges with the saved set, preserving user data.</summary>
    public static void RefreshBrowsers()
    {
        var discovered = BrowserDiscovery.DiscoverBrowsers(Core.AppInfo.ProgId);
        Config.Browsers = BrowserMerger.Merge(discovered, Config.Browsers);
        Save();
    }
}
