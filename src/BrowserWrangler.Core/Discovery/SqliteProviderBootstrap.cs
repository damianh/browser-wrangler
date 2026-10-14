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

            // Windows ships winsqlite3 in-box; elsewhere the host ships the bundled e_sqlite3 library
            if (OperatingSystem.IsWindows())
            {
                raw.SetProvider(new SQLite3Provider_winsqlite3());
            }
            else
            {
                raw.SetProvider(new SQLite3Provider_e_sqlite3());
            }

            raw.FreezeProvider();
            _initialized = true;
        }
    }
}
