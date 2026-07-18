using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using BrowserWrangler.Core.Models;
using Microsoft.Win32;

namespace BrowserWrangler.Core.Discovery;

/// <summary>
/// Discovers installed browsers from the Windows registry and enumerates their profiles.
/// Mirrors bt's discovery: SOFTWARE\Clients\StartMenuInternet in HKLM and HKCU.
/// </summary>
[SupportedOSPlatform("windows")]
public static class BrowserDiscovery
{
    private const string ClientsRoot = @"SOFTWARE\Clients\StartMenuInternet";
    private const string FirefoxInstancePrefix = "Firefox-";

    /// <summary>
    /// Discovers all browsers, ignoring the one registered with <paramref name="ignoreProgId"/>
    /// (our own registration) so we never route URLs to ourselves.
    /// </summary>
    public static List<Browser> DiscoverBrowsers(string ignoreProgId = "", bool includeFirefoxClassicProfiles = true)
    {
        var browsers = new List<Browser>();
        var instanceIds = new Dictionary<string, string>();

        DiscoverFromHive(Registry.LocalMachine, browsers, instanceIds, ignoreProgId);
        DiscoverFromHive(Registry.CurrentUser, browsers, instanceIds, ignoreProgId);

        foreach (Browser b in browsers)
        {
            ChromiumProfiles.Discover(b);
            FirefoxProfiles.Discover(
                b,
                instanceIds.GetValueOrDefault(b.Id, string.Empty),
                includeFirefoxClassicProfiles);
            AddDefaultProfileIfEmpty(b);
        }

        return browsers;
    }

    private static void DiscoverFromHive(
        RegistryKey hive,
        List<Browser> browsers,
        Dictionary<string, string> instanceIds,
        string ignoreProgId)
    {
        using RegistryKey? root = hive.OpenSubKey(ClientsRoot);
        if (root is null)
        {
            return;
        }

        foreach (string sub in root.GetSubKeyNames())
        {
            using RegistryKey? key = root.OpenSubKey(sub);
            if (key is null)
            {
                continue;
            }

            string displayName = key.GetValue(null) as string ?? sub;
            string openCommand = UnmangleOpenCommand(
                key.OpenSubKey(@"shell\open\command")?.GetValue(null) as string ?? string.Empty);
            string httpAssoc = key.OpenSubKey(@"Capabilities\URLAssociations")?.GetValue("http") as string
                ?? string.Empty;

            if (httpAssoc.Length == 0 || httpAssoc == ignoreProgId || openCommand.Length == 0)
            {
                continue;
            }

            string id = GetIdFromOpenCommand(openCommand);
            if (browsers.Any(b => b.Id == id))
            {
                continue; // HKLM & HKCU can both register the same browser
            }

            var browser = new Browser(id, displayName, openCommand) { IsAutoDiscovered = true };
            Fingerprint(openCommand, browser);
            browsers.Add(browser);
            instanceIds[id] = GetInstanceId(sub);
        }
    }

    /// <summary>Strips surrounding quotes/arguments from a shell open command, keeping the exe path.</summary>
    public static string UnmangleOpenCommand(string openCommand)
    {
        string r = openCommand;
        if (r.StartsWith('"'))
        {
            r = r[1..];
            int pos = r.IndexOf('"');
            if (pos >= 0)
            {
                r = r[..pos];
            }
        }

        return r;
    }

    /// <summary>Stable browser id: MD5 of the open command (exe names alone are not unique).</summary>
    public static string GetIdFromOpenCommand(string openCommand)
    {
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(openCommand));
        return Convert.ToHexStringLower(hash);
    }

    private static string GetInstanceId(string registryKeyName) =>
        registryKeyName.StartsWith(FirefoxInstancePrefix, StringComparison.Ordinal)
            ? registryKeyName[FirefoxInstancePrefix.Length..]
            : registryKeyName;

    /// <summary>
    /// Detects the browser engine from files next to the executable
    /// (Chromium ships *_proxy.exe, Gecko ships xul.dll) and infers the profile data folder.
    /// </summary>
    public static void Fingerprint(string exePath, Browser browser)
    {
        browser.Engine = BrowserEngine.Unknown;
        browser.DataPath = string.Empty;

        if (!File.Exists(exePath))
        {
            return;
        }

        string? folder = Path.GetDirectoryName(exePath);
        if (folder is null || !Directory.Exists(folder))
        {
            return;
        }

        string exeName = Path.GetFileName(exePath).ToLowerInvariant();
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        foreach (string file in Directory.EnumerateFiles(folder))
        {
            string name = Path.GetFileName(file);
            if (name.EndsWith("_proxy.exe", StringComparison.OrdinalIgnoreCase))
            {
                browser.Engine = BrowserEngine.Chromium;
                browser.DataPath = exeName switch
                {
                    "msedge.exe" => Path.Combine(localAppData, "Microsoft", "Edge", "User Data"),
                    "chrome.exe" when exePath.Contains(@"\Helium\", StringComparison.OrdinalIgnoreCase) =>
                        Path.Combine(localAppData, "imput", "Helium", "User Data"),
                    "chrome.exe" => Path.Combine(localAppData, "Google", "Chrome", "User Data"),
                    "vivaldi.exe" => Path.Combine(localAppData, "Vivaldi", "User Data"),
                    "brave.exe" => Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data"),
                    "thorium.exe" => Path.Combine(localAppData, "Thorium", "User Data"),
                    _ => string.Empty,
                };
                return;
            }

            if (name.Equals("xul.dll", StringComparison.OrdinalIgnoreCase))
            {
                browser.Engine = BrowserEngine.Gecko;
                browser.DataPath = exeName switch
                {
                    "firefox.exe" => Path.Combine(roamingAppData, "Mozilla", "Firefox"),
                    "waterfox.exe" => Path.Combine(roamingAppData, "Waterfox"),
                    "librewolf.exe" => Path.Combine(roamingAppData, "Librewolf"),
                    "zen.exe" => Path.Combine(roamingAppData, "zen"),
                    _ => string.Empty,
                };
                return;
            }
        }
    }

    /// <summary>Browsers with no discovered profiles get a single "Default" launch profile.</summary>
    public static void AddDefaultProfileIfEmpty(Browser browser)
    {
        if (!browser.IsAutoDiscovered || browser.Profiles.Count > 0)
        {
            return;
        }

        browser.Profiles.Add(new BrowserProfile(
            browser, "default", "Default", $"\"{BrowserProfile.UrlArgName}\"", browser.BestIconPath));
    }

    /// <summary>
    /// Returns the ProgId currently associated with a URL protocol for the current user,
    /// checking UserChoiceLatest then UserChoice (as bt does).
    /// </summary>
    public static string GetShellUrlAssociationProgId(string protocol)
    {
        string basePath = $@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\{protocol}";
        foreach (string sub in new[] { @"\UserChoiceLatest\ProgId", @"\UserChoiceLatest", @"\UserChoice" })
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(basePath + sub);
            if (key?.GetValue("ProgId") is string progId && progId.Length > 0)
            {
                return progId;
            }
        }

        return string.Empty;
    }
}
