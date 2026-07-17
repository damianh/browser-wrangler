using System.Diagnostics;
using System.Runtime.Versioning;
using BrowserWrangler.Core.Discovery;
using Microsoft.Win32;

namespace BrowserWrangler.Core.Setup;

/// <summary>A health check with an optional automatic fix (mirrors bt's system_check).</summary>
public sealed class SystemCheck
{
    public SystemCheck(
        string id,
        string name,
        string description,
        string fixDescription,
        Func<(bool Ok, string Error)> performCheck,
        Action fix)
    {
        Id = id;
        Name = name;
        Description = description;
        FixDescription = fixDescription;
        _performCheck = performCheck;
        _fix = fix;
    }

    private readonly Func<(bool Ok, string Error)> _performCheck;
    private readonly Action _fix;

    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public string FixDescription { get; }

    public bool IsOk { get; private set; }
    public string ErrorMessage { get; private set; } = string.Empty;

    public void Recheck()
    {
        (IsOk, ErrorMessage) = _performCheck();
    }

    public void Fix() => _fix();
}

/// <summary>
/// Registers/unregisters the app as a browser in the per-user registry (mirrors bt's setup).
/// </summary>
[SupportedOSPlatform("windows")]
public static class BrowserRegistration
{
    private static string BrowserRegPath => $@"Software\Clients\StartMenuInternet\{AppInfo.Name}";

    private static string CustomProtoRegPath => $@"Software\Classes\{AppInfo.CustomProtocol}";

    private static string ProgIdRegPath => $@"Software\Classes\{AppInfo.ProgId}";

    /// <summary>Full path of the executable to register; defaults to the current process.</summary>
    public static string ExecutablePath { get; set; } = Environment.ProcessPath ?? string.Empty;

    public static void RegisterAll()
    {
        RegisterCustomProtocol();
        RegisterBrowser();
    }

    public static void UnregisterAll()
    {
        Registry.CurrentUser.DeleteSubKeyTree(BrowserRegPath, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(CustomProtoRegPath, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(ProgIdRegPath, throwOnMissingSubKey: false);
        using RegistryKey? regApps = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", writable: true);
        regApps?.DeleteValue(AppInfo.Name, throwOnMissingValue: false);
    }

    /// <summary>Registers under StartMenuInternet with http/https capabilities.</summary>
    public static void RegisterBrowser()
    {
        string appPath = ExecutablePath;
        string capRoot = BrowserRegPath + @"\Capabilities";

        SetValue(BrowserRegPath, null, AppInfo.Name);

        SetValue(capRoot, "ApplicationName", AppInfo.Name);
        SetValue(capRoot, "ApplicationDescription", AppInfo.Description);
        SetValue(capRoot, "ApplicationIcon", appPath + ",0");

        foreach (string protocol in new[] { "https", "http", AppInfo.CustomProtocol })
        {
            SetValue(capRoot + @"\URLAssociations", protocol, AppInfo.ProgId);
        }

        SetValue(BrowserRegPath + @"\DefaultIcon", null, appPath + ",0");
        SetValue(BrowserRegPath + @"\shell\open\command", null, $"\"{appPath}\"");

        // the ProgId that http/https associations point at
        SetValue(ProgIdRegPath, null, $"{AppInfo.Name} Document");
        SetValue(ProgIdRegPath + @"\DefaultIcon", null, appPath + ",0");
        SetValue(ProgIdRegPath + @"\Application", "ApplicationName", AppInfo.Name);
        SetValue(ProgIdRegPath + @"\Application", "ApplicationDescription", AppInfo.Description);
        SetValue(ProgIdRegPath + @"\shell\open\command", null, $"\"{appPath}\" \"%1\"");

        SetValue(@"Software\RegisteredApplications", AppInfo.Name, capRoot);
    }

    /// <summary>Registers the x-bw custom protocol handler.</summary>
    public static void RegisterCustomProtocol()
    {
        SetValue(CustomProtoRegPath, null, $"URL:{AppInfo.CustomProtocol}");
        SetValue(CustomProtoRegPath, "URL Protocol", string.Empty);
        SetValue(CustomProtoRegPath + @"\shell\open\command", null, $"\"{ExecutablePath}\" \"%1\"");
    }

    public static bool IsRegisteredAsBrowser(out string error)
    {
        string expected = $"\"{ExecutablePath}\"";
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(BrowserRegPath + @"\shell\open\command");
        string actual = key?.GetValue(null) as string ?? string.Empty;
        bool ok = expected == actual;
        error = ok ? string.Empty : $"Expected: {expected}\nRegistered: {actual}";
        return ok;
    }

    /// <summary>True when http/https default handlers point at our ProgId.</summary>
    public static bool IsDefaultBrowser(out bool http, out bool https)
    {
        http = BrowserDiscovery.GetShellUrlAssociationProgId("http") == AppInfo.ProgId;
        https = BrowserDiscovery.GetShellUrlAssociationProgId("https") == AppInfo.ProgId;
        return http && https;
    }

    /// <summary>Opens Windows Settings on the default apps page.</summary>
    public static void OpenDefaultAppsSettings()
    {
        Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
    }

    public static List<SystemCheck> GetChecks() =>
    [
        new SystemCheck(
            "sys_browser",
            "System Browser",
            "Registered as a virtual browser in Windows.",
            "automatically register as a virtual browser",
            () =>
            {
                bool ok = IsRegisteredAsBrowser(out string error);
                return (ok, error);
            },
            RegisterAll),
        new SystemCheck(
            "proto_http",
            "HTTP Protocol Handler",
            "Once set, Windows will forward HTTP links to it.",
            "open system settings and set the default browser",
            () =>
            {
                IsDefaultBrowser(out bool http, out _);
                string current = BrowserDiscovery.GetShellUrlAssociationProgId("http");
                return (http, http ? string.Empty : $"Current handler is {current}.");
            },
            OpenDefaultAppsSettings),
        new SystemCheck(
            "proto_https",
            "HTTPS Protocol Handler",
            "Once set, Windows will forward HTTPS links to it.",
            "open system settings and set the default browser",
            () =>
            {
                IsDefaultBrowser(out _, out bool https);
                string current = BrowserDiscovery.GetShellUrlAssociationProgId("https");
                return (https, https ? string.Empty : $"Current handler is {current}.");
            },
            OpenDefaultAppsSettings),
    ];

    private static void SetValue(string keyPath, string? valueName, string value)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(keyPath);
        key.SetValue(valueName ?? string.Empty, value);
    }
}
