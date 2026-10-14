using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using BrowserWrangler.Core.Setup;

namespace BrowserWrangler.Core.Platform.Linux;

/// <summary>
/// Registers the app as a browser via a per-user .desktop file and makes it the default http(s)
/// handler in mimeapps.list. Unlike Windows, Linux lets an app set itself as default.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxBrowserRegistration(XdgDirectories dirs)
{
    /// <summary>Overrides the Exec command, e.g. a stable wrapper name for packaged (Nix) installs.</summary>
    public const string ExecOverrideVariable = "BROWSER_WRANGLER_EXEC";

    public static readonly string[] HandledSchemes =
        ["x-scheme-handler/http", "x-scheme-handler/https", $"x-scheme-handler/{AppInfo.CustomProtocol}"];

    private const string HttpScheme = "x-scheme-handler/http";
    private const string HttpsScheme = "x-scheme-handler/https";

    public string DesktopFilePath => Path.Combine(dirs.UserApplicationsDir, AppInfo.LinuxDesktopFileName);

    private string IconFilePath =>
        Path.Combine(dirs.DataHome, "icons", "hicolor", "256x256", "apps", AppInfo.LinuxAppId + ".png");

    private string UserMimeAppsPath => Path.Combine(dirs.ConfigHome, "mimeapps.list");

    /// <summary>The command written to Exec: override, AppImage path or the running executable.</summary>
    public static string ExecutablePath
    {
        get
        {
            string? value = Environment.GetEnvironmentVariable(ExecOverrideVariable);
            if (string.IsNullOrEmpty(value))
            {
                value = Environment.GetEnvironmentVariable("APPIMAGE");
            }

            return string.IsNullOrEmpty(value) ? Environment.ProcessPath ?? string.Empty : value;
        }
    }

    public static string BuildDesktopFile(string executablePath, string icon) =>
        new StringBuilder()
            .Append("[Desktop Entry]\n")
            .Append("Type=Application\n")
            .Append("Version=1.5\n")
            .Append("Name=").Append(DesktopEntry.EscapeString(AppInfo.Name)).Append('\n')
            .Append("GenericName=Web Browser\n")
            .Append("Comment=").Append(DesktopEntry.EscapeString(AppInfo.Description)).Append('\n')
            .Append("Exec=").Append(DesktopEntry.EscapeString(BuildExec(executablePath))).Append('\n')
            .Append("Icon=").Append(DesktopEntry.EscapeString(icon)).Append('\n')
            .Append("Terminal=false\n")
            .Append("StartupNotify=false\n")
            .Append("StartupWMClass=").Append(AppInfo.LinuxAppId).Append('\n')
            .Append("Categories=Network;WebBrowser;\n")
            .Append("MimeType=").Append(string.Join(';', HandledSchemes)).Append(";\n")
            .ToString();

    public static string BuildExec(string executablePath) => DesktopEntry.QuoteExecArgument(executablePath) + " %u";

    public void Register()
    {
        Directory.CreateDirectory(dirs.UserApplicationsDir);
        string icon = InstallIcon();
        WriteAtomically(DesktopFilePath, BuildDesktopFile(ExecutablePath, icon));
        RunBestEffort("update-desktop-database", dirs.UserApplicationsDir);
    }

    public void Unregister()
    {
        TryDelete(DesktopFilePath);
        TryDelete(IconFilePath);
        if (File.Exists(UserMimeAppsPath))
        {
            string content = File.ReadAllText(UserMimeAppsPath);
            WriteAtomically(UserMimeAppsPath, MimeAppsList.RemoveDefault(content, HandledSchemes, AppInfo.LinuxDesktopFileName));
        }

        RunBestEffort("update-desktop-database", dirs.UserApplicationsDir);
    }

    public bool EnsureRegistered()
    {
        try
        {
            if (ExecutablePath.Length == 0 || IsRegisteredAsBrowser(out _))
            {
                return false;
            }

            Register();
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Our desktop id must resolve (user file or a packaged one, e.g. from Nix) and launch the
    /// expected executable.
    /// </summary>
    public bool IsRegisteredAsBrowser(out string error)
    {
        DesktopEntry? entry = FindOwnDesktopEntry();
        if (entry is null)
        {
            error = $"{AppInfo.LinuxDesktopFileName} not found.";
            return false;
        }

        List<string> exec = DesktopEntry.SplitExec(entry.Exec);
        string actual = exec.Count > 0 ? exec[0] : string.Empty;
        string expected = ExecutablePath;
        bool ok = actual == expected;
        error = ok ? string.Empty : $"Expected: {expected}\nRegistered: {actual} ({entry.FilePath})";
        return ok;
    }

    public string GetDefaultHandler(string scheme) =>
        MimeAppsList.QueryDefault(dirs, scheme, IsInstalled);

    public bool IsDefaultBrowser(out bool http, out bool https)
    {
        http = GetDefaultHandler(HttpScheme) == AppInfo.LinuxDesktopFileName;
        https = GetDefaultHandler(HttpsScheme) == AppInfo.LinuxDesktopFileName;
        return http && https;
    }

    /// <summary>
    /// Writes the defaults to the user mimeapps.list (read by GNOME, KDE and xdg-open) and, on
    /// Plasma, kdeglobals' BrowserApplication which some KDE apps still consult.
    /// </summary>
    public void MakeDefaultBrowser()
    {
        EnsureRegistered();
        string content = File.Exists(UserMimeAppsPath) ? File.ReadAllText(UserMimeAppsPath) : string.Empty;
        Directory.CreateDirectory(dirs.ConfigHome);
        WriteAtomically(UserMimeAppsPath, MimeAppsList.SetDefault(content, HandledSchemes, AppInfo.LinuxDesktopFileName));

        // a desktop-specific list would shadow ours, so update those too when they exist
        foreach (string desktop in dirs.CurrentDesktops)
        {
            string desktopList = Path.Combine(dirs.ConfigHome, $"{desktop}-mimeapps.list");
            if (File.Exists(desktopList))
            {
                string desktopContent = File.ReadAllText(desktopList);
                if (HandledSchemes.Any(s => MimeAppsList.GetDefaults(desktopContent, s).Count > 0))
                {
                    WriteAtomically(desktopList, MimeAppsList.SetDefault(desktopContent, HandledSchemes, AppInfo.LinuxDesktopFileName));
                }
            }
        }

        if (dirs.CurrentDesktops.Contains("kde"))
        {
            bool done = RunBestEffort("kwriteconfig6", "--file", "kdeglobals", "--group", "General", "--key", "BrowserApplication", AppInfo.LinuxDesktopFileName);
            if (!done)
            {
                RunBestEffort("kwriteconfig5", "--file", "kdeglobals", "--group", "General", "--key", "BrowserApplication", AppInfo.LinuxDesktopFileName);
            }
        }
    }

    public List<SystemCheck> GetChecks() =>
    [
        new SystemCheck(
            "sys_browser",
            "System Browser",
            $"Registered as a browser ({AppInfo.LinuxDesktopFileName}).",
            "write the .desktop file",
            () => (IsRegisteredAsBrowser(out string error), error),
            Register),
        new SystemCheck(
            "proto_http",
            "HTTP Protocol Handler",
            "Once set, HTTP links opened by other apps come here.",
            "make Browser Wrangler the default browser",
            () =>
            {
                IsDefaultBrowser(out bool http, out _);
                return (http, http ? string.Empty : $"Current handler is {Describe(GetDefaultHandler(HttpScheme))}.");
            },
            MakeDefaultBrowser),
        new SystemCheck(
            "proto_https",
            "HTTPS Protocol Handler",
            "Once set, HTTPS links opened by other apps come here.",
            "make Browser Wrangler the default browser",
            () =>
            {
                IsDefaultBrowser(out _, out bool https);
                return (https, https ? string.Empty : $"Current handler is {Describe(GetDefaultHandler(HttpsScheme))}.");
            },
            MakeDefaultBrowser),
    ];

    private static string Describe(string desktopId) => desktopId.Length > 0 ? desktopId : "not set";

    private DesktopEntry? FindOwnDesktopEntry()
    {
        foreach (string dir in dirs.ApplicationDirs)
        {
            string path = Path.Combine(dir, AppInfo.LinuxDesktopFileName);
            if (File.Exists(path))
            {
                return DesktopEntry.TryLoad(AppInfo.LinuxDesktopFileName, path);
            }
        }

        return null;
    }

    private bool IsInstalled(string desktopId)
    {
        foreach (string dir in dirs.ApplicationDirs)
        {
            if (File.Exists(Path.Combine(dir, desktopId)) ||
                File.Exists(Path.Combine(dir, desktopId.Replace('-', Path.DirectorySeparatorChar))))
            {
                return true;
            }
        }

        return false;
    }

    private string InstallIcon()
    {
        string source = Path.Combine(AppContext.BaseDirectory, "logo-256.png");
        if (!File.Exists(source))
        {
            return "web-browser";
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(IconFilePath)!);
            File.Copy(source, IconFilePath, overwrite: true);
            return AppInfo.LinuxAppId;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return source;
        }
    }

    private static void WriteAtomically(string path, string content)
    {
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static bool RunBestEffort(string fileName, params string[] args)
    {
        if (LinuxBrowserDiscovery.FindOnPath(fileName) is null)
        {
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo(fileName)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (string arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

            using Process? process = Process.Start(psi);
            return process is not null && process.WaitForExit(3000) && process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
