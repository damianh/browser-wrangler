using BrowserWrangler.Core.Discovery;
using BrowserWrangler.Core.Launching;
using BrowserWrangler.Core.Models;

namespace BrowserWrangler.Core.Platform.Linux;

/// <summary>
/// Discovers browsers from .desktop files that handle http(s) links - this covers distro
/// packages, Flatpak and Snap exports and Nix profiles alike - then fingerprints the engine
/// and profile folder from the desktop id / executable name.
/// </summary>
public static class LinuxBrowserDiscovery
{
    private const string HttpScheme = "x-scheme-handler/http";
    private const string HttpsScheme = "x-scheme-handler/https";

    private sealed record KnownBrowser(BrowserEngine Engine, string[] Keys, string[] DataPaths);

    // DataPaths are relative to $HOME for native installs; Flatpak/Snap variants are derived.
    private static readonly KnownBrowser[] KnownBrowsers =
    [
        new(BrowserEngine.Chromium, ["google-chrome", "google-chrome-stable", "com.google.chrome"], [".config/google-chrome"]),
        new(BrowserEngine.Chromium, ["google-chrome-beta", "com.google.chromebeta"], [".config/google-chrome-beta"]),
        new(BrowserEngine.Chromium, ["google-chrome-unstable", "com.google.chromedev"], [".config/google-chrome-unstable"]),
        new(BrowserEngine.Chromium, ["chromium", "chromium-browser", "org.chromium.chromium", "ungoogled-chromium",
            "io.github.ungoogled_software.ungoogled_chromium"], [".config/chromium"]),
        new(BrowserEngine.Chromium, ["microsoft-edge", "microsoft-edge-stable", "com.microsoft.edge"], [".config/microsoft-edge"]),
        new(BrowserEngine.Chromium, ["microsoft-edge-beta"], [".config/microsoft-edge-beta"]),
        new(BrowserEngine.Chromium, ["microsoft-edge-dev"], [".config/microsoft-edge-dev"]),
        new(BrowserEngine.Chromium, ["brave-browser", "brave", "com.brave.browser"], [".config/BraveSoftware/Brave-Browser"]),
        new(BrowserEngine.Chromium, ["vivaldi", "vivaldi-stable", "com.vivaldi.vivaldi"], [".config/vivaldi"]),
        new(BrowserEngine.Chromium, ["thorium-browser", "thorium"], [".config/thorium"]),
        new(BrowserEngine.Chromium, ["helium", "helium-browser", "net.imput.helium"], [".config/net.imput.helium"]),
        new(BrowserEngine.Gecko, ["firefox", "firefox-esr", "firefox-bin", "firefox-beta", "firefox-nightly",
            "firefox-developer-edition", "firefox-devedition", "org.mozilla.firefox"],
            [".mozilla/firefox", ".config/mozilla/firefox"]),
        new(BrowserEngine.Gecko, ["librewolf", "io.gitlab.librewolf-community"], [".librewolf", ".config/librewolf/librewolf"]),
        new(BrowserEngine.Gecko, ["waterfox", "net.waterfox.waterfox"], [".waterfox"]),
        new(BrowserEngine.Gecko, ["zen", "zen-browser", "zen-beta", "app.zen_browser.zen"], [".zen"]),
        new(BrowserEngine.Gecko, ["floorp", "one.ablaze.floorp"], [".floorp"]),
    ];

    public static List<Browser> DiscoverBrowsers(XdgDirectories dirs, bool includeFirefoxClassicProfiles = true)
    {
        var browsers = new List<Browser>();
        foreach (DesktopEntry entry in FindUrlHandlers(dirs))
        {
            List<string> command = DesktopEntry.SplitExec(entry.Exec);
            if (command.Count == 0)
            {
                continue;
            }

            command[0] = StabilizeNixStorePath(command[0], FindOnPath);

            string openCommand = CommandLine.Join(command);
            string id = BrowserDiscovery.GetIdFromOpenCommand(openCommand);
            if (browsers.Any(b => b.Id == id))
            {
                continue;
            }

            var browser = new Browser(id, entry.Name.Length > 0 ? entry.Name : entry.Id, openCommand)
            {
                IsAutoDiscovered = true,
                IconPath = IconThemeLookup.Resolve(entry.Icon, dirs),
            };
            Fingerprint(browser, entry.Id, command, dirs.Home);

            ChromiumProfiles.Discover(browser);
            FirefoxProfiles.Discover(browser, string.Empty, includeFirefoxClassicProfiles);
            BrowserDiscovery.AddDefaultProfileIfEmpty(browser);
            browsers.Add(browser);
        }

        return browsers;
    }

    /// <summary>
    /// Visible desktop entries that handle http or https, honouring the spec's precedence
    /// (the first directory providing a desktop id wins, even when it hides the entry).
    /// </summary>
    public static IEnumerable<DesktopEntry> FindUrlHandlers(XdgDirectories dirs)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string appDir in dirs.ApplicationDirs)
        {
            if (!Directory.Exists(appDir))
            {
                continue;
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(appDir, "*.desktop", SearchOption.AllDirectories).Order().ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string file in files)
            {
                string id = Path.GetRelativePath(appDir, file).Replace(Path.DirectorySeparatorChar, '-');
                if (!seen.Add(id) || IsOwnDesktopId(id))
                {
                    continue;
                }

                DesktopEntry? entry = DesktopEntry.TryLoad(id, file);
                if (entry is null || entry.IsHidden || entry.NoDisplay || entry.Type != "Application" ||
                    !entry.MimeTypes.Any(m => m is HttpScheme or HttpsScheme))
                {
                    continue;
                }

                yield return entry;
            }
        }
    }

    /// <summary>
    /// Nix store paths change with every update, which would make the browser look new and drop
    /// its rules. Prefer the bare executable name when it resolves on PATH.
    /// </summary>
    public static string StabilizeNixStorePath(string executable, Func<string, string?> findOnPath)
    {
        if (!executable.StartsWith("/nix/store/", StringComparison.Ordinal))
        {
            return executable;
        }

        string name = Path.GetFileName(executable);
        return findOnPath(name) is not null ? name : executable;
    }

    public static string? FindOnPath(string name)
    {
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(dir, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static bool IsOwnDesktopId(string desktopId) =>
        desktopId.StartsWith("browser-wrangler", StringComparison.OrdinalIgnoreCase);

    /// <summary>Sets <see cref="Browser.Engine"/> and <see cref="Browser.DataPath"/> for known browsers.</summary>
    public static void Fingerprint(Browser browser, string desktopId, IReadOnlyList<string> command, string home)
    {
        browser.Engine = BrowserEngine.Unknown;
        browser.DataPath = string.Empty;

        string? flatpakId = GetFlatpakAppId(command);
        string executable = Path.GetFileName(command[0]);
        string? snapName = command[0].StartsWith("/snap/bin/", StringComparison.Ordinal) ? executable : null;

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFileNameWithoutExtension(desktopId),
            executable,
        };
        if (flatpakId is not null)
        {
            keys.Add(flatpakId);
        }

        KnownBrowser? known = KnownBrowsers.FirstOrDefault(k => k.Keys.Any(keys.Contains));
        if (known is null)
        {
            return;
        }

        List<string> candidates = [];
        foreach (string relative in known.DataPaths)
        {
            if (flatpakId is not null)
            {
                // Flatpak maps XDG_CONFIG_HOME to ~/.var/app/<id>/config; dot-folders persist under ~/.var/app/<id>
                string mapped = relative.StartsWith(".config/", StringComparison.Ordinal) ? relative[1..] : relative;
                candidates.Add(Path.Combine(home, ".var", "app", flatpakId, mapped));
            }
            else if (snapName is not null)
            {
                string snapRoot = Path.Combine(home, "snap", snapName);
                candidates.Add(Path.Combine(snapRoot, "common", relative));
                if (relative.StartsWith(".config/", StringComparison.Ordinal))
                {
                    candidates.Add(Path.Combine(snapRoot, "common", relative[".config/".Length..]));
                }

                candidates.Add(Path.Combine(snapRoot, "current", relative));
            }
            else
            {
                candidates.Add(Path.Combine(home, relative));
            }
        }

        browser.Engine = known.Engine;
        browser.DataPath = candidates.FirstOrDefault(Directory.Exists) ?? candidates[0];
    }

    /// <summary>Returns the app id from "flatpak run [options] app.id ..." commands.</summary>
    public static string? GetFlatpakAppId(IReadOnlyList<string> command)
    {
        if (Path.GetFileName(command[0]) != "flatpak")
        {
            return null;
        }

        int run = -1;
        for (int i = 1; i < command.Count; i++)
        {
            if (command[i] == "run")
            {
                run = i;
                break;
            }
        }

        if (run < 0)
        {
            return null;
        }

        return command.Skip(run + 1).FirstOrDefault(a => !a.StartsWith('-'));
    }
}
