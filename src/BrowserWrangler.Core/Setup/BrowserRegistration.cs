using System.Diagnostics;
using System.Runtime.InteropServices;
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

    public static void RegisterAll(bool includeHtmlFileAssociations = false)
    {
        RegisterCustomProtocol();
        RegisterBrowser(includeHtmlFileAssociations);
        NotifyShellAssociationsChanged();
    }

    public static void UnregisterAll()
    {
        Registry.CurrentUser.DeleteSubKeyTree(BrowserRegPath, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(CustomProtoRegPath, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(ProgIdRegPath, throwOnMissingSubKey: false);
        using RegistryKey? regApps = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", writable: true);
        regApps?.DeleteValue(AppInfo.Name, throwOnMissingValue: false);
        NotifyShellAssociationsChanged();
    }

    /// <summary>
    /// Re-registers when the registration is missing or points at a different executable
    /// (moved install, upgrade, portable copy). Safe to call on the URL-open hot path: it is a
    /// single registry read in the common case and never throws.
    /// </summary>
    /// <returns>True when a repair was performed.</returns>
    public static bool EnsureRegistered(bool includeHtmlFileAssociations = false)
    {
        try
        {
            if (ExecutablePath.Length == 0)
            {
                return false;
            }

            if (IsRegisteredAsBrowser(out _) && HasExpectedHtmlFileAssociations(includeHtmlFileAssociations))
            {
                return false;
            }

            RegisterAll(includeHtmlFileAssociations);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            // registration is best-effort - never break URL routing over it
            return false;
        }
    }

    public static IReadOnlyList<string> GetRegisteredUrlProtocols() =>
    [
        "https",
        "http",
        AppInfo.CustomProtocol,
    ];

    public static IReadOnlyList<string> GetRegisteredFileExtensions(bool includeHtmlFileAssociations) =>
        includeHtmlFileAssociations
            ?
            [
                ".htm",
                ".html",
            ]
            : [];

    /// <summary>Registers under StartMenuInternet with URL capabilities and optional HTML file associations.</summary>
    public static void RegisterBrowser(bool includeHtmlFileAssociations = false)
    {
        string appPath = ExecutablePath;
        string capRoot = BrowserRegPath + @"\Capabilities";
        string fileAssociationsPath = capRoot + @"\FileAssociations";

        SetValue(BrowserRegPath, null, AppInfo.Name);

        SetValue(capRoot, "ApplicationName", AppInfo.Name);
        SetValue(capRoot, "ApplicationDescription", AppInfo.Description);
        SetValue(capRoot, "ApplicationIcon", appPath + ",0");

        foreach (string protocol in GetRegisteredUrlProtocols())
        {
            SetValue(capRoot + @"\URLAssociations", protocol, AppInfo.ProgId);
        }

        Registry.CurrentUser.DeleteSubKeyTree(fileAssociationsPath, throwOnMissingSubKey: false);
        foreach (string extension in GetRegisteredFileExtensions(includeHtmlFileAssociations))
        {
            SetValue(fileAssociationsPath, extension, AppInfo.ProgId);
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

    private static bool HasExpectedHtmlFileAssociations(bool includeHtmlFileAssociations)
    {
        using RegistryKey? fileAssociations = Registry.CurrentUser.OpenSubKey(BrowserRegPath + @"\Capabilities\FileAssociations");
        IReadOnlyList<string> extensions = GetRegisteredFileExtensions(includeHtmlFileAssociations);
        if (extensions.Count == 0)
        {
            return fileAssociations is null;
        }

        if (fileAssociations is null)
        {
            return false;
        }

        foreach (string extension in extensions)
        {
            if (!string.Equals(fileAssociations.GetValue(extension) as string, AppInfo.ProgId, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>True when http/https default handlers point at our ProgId.</summary>
    public static bool IsDefaultBrowser(out bool http, out bool https)
    {
        http = BrowserDiscovery.GetShellUrlAssociationProgId("http") == AppInfo.ProgId;
        https = BrowserDiscovery.GetShellUrlAssociationProgId("https") == AppInfo.ProgId;
        return http && https;
    }

    /// <summary>Opens Windows Settings on the default apps page, scrolled to our entry where supported.</summary>
    public static void OpenDefaultAppsSettings()
    {
        Process.Start(new ProcessStartInfo(BuildDefaultAppsUri(Environment.OSVersion.Version.Build))
        {
            UseShellExecute = true,
        });
    }

    /// <summary>
    /// Builds the ms-settings URI for the Default Apps page. Windows 11 (build 22000+) supports
    /// deep-linking straight to an app's entry; Windows 10 only has the page itself.
    /// </summary>
    public static string BuildDefaultAppsUri(int osBuild)
    {
        const string page = "ms-settings:defaultapps";
        return osBuild >= 22000
            ? $"{page}?registeredAppUser={Uri.EscapeDataString(AppInfo.Name)}"
            : page;
    }

    /// <summary>Tells the shell that file/URL associations changed so Default Apps refreshes.</summary>
    private static void NotifyShellAssociationsChanged()
    {
        try
        {
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        }
        catch (DllNotFoundException)
        {
            // notification is a nicety; registration itself already succeeded
        }
    }

    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;

    [DllImport("shell32.dll", SetLastError = false)]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

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
            () => RegisterAll()),
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
