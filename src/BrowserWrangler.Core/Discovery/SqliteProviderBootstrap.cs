using SQLitePCL;

namespace BrowserWrangler.Core.Discovery;

public static class SqliteProviderBootstrap
{
    private static readonly object ProviderLock = new();
    private static bool _initialized;

    public static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        lock (ProviderLock)
        {
            if (_initialized)
            {
                return;
            }

            raw.SetProvider(new SQLite3Provider_winsqlite3());
            raw.FreezeProvider();
            _initialized = true;
        }
    }
}
