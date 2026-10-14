using BrowserWrangler.Core.Configuration;
using BrowserWrangler.Core.Launching;
using BrowserWrangler.Core.Models;
using BrowserWrangler.Core.Platform;
using BrowserWrangler.Core.Platform.Linux;

namespace BrowserWrangler.Core.Tests;

public class DesktopEntryTests
{
    [Theory]
    [InlineData("firefox %u", new[] { "firefox" })]
    [InlineData("/usr/bin/google-chrome-stable --incognito %U", new[] { "/usr/bin/google-chrome-stable", "--incognito" })]
    [InlineData("\"/opt/My Browser/browser\" --flag", new[] { "/opt/My Browser/browser", "--flag" })]
    [InlineData("\"/opt/a \\\"b\\\" \\$c\"", new[] { "/opt/a \"b\" $c" })]
    [InlineData("app --pct=100%% %f", new[] { "app", "--pct=100%" })]
    [InlineData(
        "/usr/bin/flatpak run --branch=stable --arch=x86_64 --command=firefox --file-forwarding org.mozilla.firefox @@u %u @@",
        new[] { "/usr/bin/flatpak", "run", "--branch=stable", "--arch=x86_64", "--command=firefox", "--file-forwarding", "org.mozilla.firefox" })]
    public void SplitExec_applies_spec_quoting_and_drops_field_codes(string exec, string[] expected) =>
        Assert.Equal(expected, DesktopEntry.SplitExec(exec));

    [Theory]
    [InlineData("/usr/bin/browser-wrangler")]
    [InlineData("/home/me/Apps/Browser Wrangler.AppImage")]
    [InlineData("/tmp/we\"ird$`\\path")]
    public void QuoteExecArgument_round_trips(string path) =>
        Assert.Equal([path], DesktopEntry.SplitExec(DesktopEntry.QuoteExecArgument(path) + " %u"));

    [Fact]
    public void Parse_reads_main_group_only_and_unescapes()
    {
        const string content = """
            # comment
            [Desktop Entry]
            Name=Firefox
            Name[de]=Feuerfuchs
            Exec=firefox %u
            Comment=line\nbreak
            MimeType=text/html;x-scheme-handler/http;x-scheme-handler/https;
            NoDisplay=false

            [Desktop Action new-window]
            Name=New Window
            Exec=firefox --new-window %u
            """;

        DesktopEntry entry = DesktopEntry.Parse("firefox.desktop", "/x/firefox.desktop", content)!;

        Assert.Equal("Firefox", entry.Name);
        Assert.Equal("firefox %u", entry.Exec);
        Assert.Equal("line\nbreak", entry.Get("Comment"));
        Assert.False(entry.NoDisplay);
        Assert.Contains("x-scheme-handler/https", entry.MimeTypes);
    }

    [Fact]
    public void Parse_returns_null_without_desktop_entry_group() =>
        Assert.Null(DesktopEntry.Parse("x.desktop", "/x.desktop", "[Other]\nName=x\n"));
}

public class CommandLineTests
{
    public static TheoryData<string[]> RoundTripCases => new()
    {
        new[] { "flatpak", "run", "org.mozilla.firefox" },
        new[] { "/opt/My Browser/browser", "--flag=a b", "" },
        new[] { "x", "quote\"inside", "trailing\\", "back\\\\slash\\\"q" },
    };

    [Theory]
    [MemberData(nameof(RoundTripCases))]
    public void Join_and_Split_round_trip(string[] args) =>
        Assert.Equal(args, CommandLine.Split(CommandLine.Join(args)));

    [Fact]
    public void CreateStartInfo_splits_program_from_prefix_args_on_unix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var psi = BrowserLauncher.CreateStartInfo("flatpak run org.mozilla.firefox", "\"https://a.b/?q=1\" -P \"work\"");

        Assert.Equal("flatpak", psi.FileName);
        Assert.Equal(["run", "org.mozilla.firefox", "https://a.b/?q=1", "-P", "work"], CommandLine.Split(psi.Arguments));
    }
}

public class MimeAppsListTests
{
    private static readonly string[] Schemes = ["x-scheme-handler/http", "x-scheme-handler/https"];

    [Fact]
    public void SetDefault_creates_group_and_entries()
    {
        string result = MimeAppsList.SetDefault(string.Empty, Schemes, "bw.desktop");

        Assert.Equal(["bw.desktop"], MimeAppsList.GetDefaults(result, "x-scheme-handler/http"));
        Assert.Equal(["bw.desktop"], MimeAppsList.GetDefaults(result, "x-scheme-handler/https"));
    }

    [Fact]
    public void SetDefault_prepends_and_keeps_other_groups_and_types()
    {
        const string content = """
            [Added Associations]
            x-scheme-handler/http=firefox.desktop;

            [Default Applications]
            text/html=firefox.desktop;
            x-scheme-handler/http=firefox.desktop;bw.desktop;

            """;

        string result = MimeAppsList.SetDefault(content, Schemes, "bw.desktop");

        Assert.Equal(["bw.desktop", "firefox.desktop"], MimeAppsList.GetDefaults(result, "x-scheme-handler/http"));
        Assert.Equal(["bw.desktop"], MimeAppsList.GetDefaults(result, "x-scheme-handler/https"));
        Assert.Equal(["firefox.desktop"], MimeAppsList.GetDefaults(result, "text/html"));
        Assert.Contains("[Added Associations]\nx-scheme-handler/http=firefox.desktop;", result);
    }

    [Fact]
    public void RemoveDefault_drops_id_and_empty_entries()
    {
        string content = MimeAppsList.SetDefault("[Default Applications]\nx-scheme-handler/http=firefox.desktop;\n", Schemes, "bw.desktop");

        string result = MimeAppsList.RemoveDefault(content, Schemes, "bw.desktop");

        Assert.Equal(["firefox.desktop"], MimeAppsList.GetDefaults(result, "x-scheme-handler/http"));
        Assert.Empty(MimeAppsList.GetDefaults(result, "x-scheme-handler/https"));
        Assert.DoesNotContain("x-scheme-handler/https", result);
    }

    [Fact]
    public void QueryDefault_honours_file_precedence_and_installed_ids()
    {
        using var fx = new XdgFixture("kde");
        fx.Write("config/kde-mimeapps.list", "[Default Applications]\nx-scheme-handler/https=missing.desktop;\n");
        fx.Write("config/mimeapps.list", "[Default Applications]\nx-scheme-handler/https=brave-browser.desktop;\n");
        fx.Write("sys/applications/mimeapps.list", "[Default Applications]\nx-scheme-handler/https=firefox.desktop;\n");

        string id = MimeAppsList.QueryDefault(fx.Dirs, "x-scheme-handler/https", d => d != "missing.desktop");

        Assert.Equal("brave-browser.desktop", id);
    }
}

public class LinuxBrowserDiscoveryTests
{
    private const string Http = "MimeType=text/html;x-scheme-handler/http;x-scheme-handler/https;\n";

    [Fact]
    public void FindUrlHandlers_respects_precedence_visibility_and_mime_types()
    {
        using var fx = new XdgFixture();
        fx.Write("data/applications/firefox.desktop", "[Desktop Entry]\nType=Application\nName=Firefox (user)\nExec=firefox %u\n" + Http);
        fx.Write("sys/applications/firefox.desktop", "[Desktop Entry]\nType=Application\nName=Firefox\nExec=firefox %u\n" + Http);
        fx.Write("data/applications/chromium.desktop", "[Desktop Entry]\nType=Application\nHidden=true\n");
        fx.Write("sys/applications/chromium.desktop", "[Desktop Entry]\nType=Application\nName=Chromium\nExec=chromium %U\n" + Http);
        fx.Write("sys/applications/gimp.desktop", "[Desktop Entry]\nType=Application\nName=GIMP\nExec=gimp %U\nMimeType=image/png;\n");
        fx.Write("sys/applications/browser-wrangler.desktop", "[Desktop Entry]\nType=Application\nName=BW\nExec=bw %u\n" + Http);
        fx.Write("sys/applications/vendor/brave.desktop", "[Desktop Entry]\nType=Application\nName=Brave\nExec=brave %U\n" + Http);

        var handlers = LinuxBrowserDiscovery.FindUrlHandlers(fx.Dirs).ToList();

        Assert.Equal(["Firefox (user)", "Brave"], handlers.Select(h => h.Name));
        Assert.Equal("vendor-brave.desktop", handlers[1].Id);
    }

    [Fact]
    public void DiscoverBrowsers_builds_stable_ids_and_detects_engines()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var fx = new XdgFixture();
        fx.Write("sys/applications/google-chrome.desktop", "[Desktop Entry]\nType=Application\nName=Google Chrome\nExec=/usr/bin/google-chrome-stable %U\n" + Http);
        fx.Write("sys/applications/org.mozilla.firefox.desktop",
            "[Desktop Entry]\nType=Application\nName=Firefox\nExec=/usr/bin/flatpak run --branch=stable --command=firefox --file-forwarding org.mozilla.firefox @@u %u @@\n" + Http);

        var browsers = LinuxBrowserDiscovery.DiscoverBrowsers(fx.Dirs);

        Browser chrome = browsers.Single(b => b.Name == "Google Chrome");
        Assert.Equal("/usr/bin/google-chrome-stable", chrome.OpenCommand);
        Assert.Equal(BrowserEngine.Chromium, chrome.Engine);
        Assert.Equal(Path.Combine(fx.Home, ".config", "google-chrome"), chrome.DataPath);
        Assert.NotEmpty(chrome.Profiles);

        Browser firefox = browsers.Single(b => b.Name == "Firefox");
        Assert.Equal("/usr/bin/flatpak run --branch=stable --command=firefox --file-forwarding org.mozilla.firefox", firefox.OpenCommand);
        Assert.Equal(BrowserEngine.Gecko, firefox.Engine);
        Assert.Equal(Path.Combine(fx.Home, ".var", "app", "org.mozilla.firefox", ".mozilla", "firefox"), firefox.DataPath);

        Assert.Equal(browsers.Select(b => b.Id), LinuxBrowserDiscovery.DiscoverBrowsers(fx.Dirs).Select(b => b.Id));
    }

    [Theory]
    [InlineData("com.google.Chrome", ".var/app/com.google.Chrome/config/google-chrome")]
    [InlineData("com.brave.Browser", ".var/app/com.brave.Browser/config/BraveSoftware/Brave-Browser")]
    public void Fingerprint_maps_flatpak_config_dir(string appId, string expected)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var browser = new Browser("x", "x", "x");
        LinuxBrowserDiscovery.Fingerprint(browser, appId + ".desktop", ["/usr/bin/flatpak", "run", appId], "/home/me");

        Assert.Equal(BrowserEngine.Chromium, browser.Engine);
        Assert.Equal(Path.Combine("/home/me", expected), browser.DataPath);
    }

    [Fact]
    public void Fingerprint_maps_snap_paths()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var browser = new Browser("x", "x", "x");
        LinuxBrowserDiscovery.Fingerprint(browser, "firefox_firefox.desktop", ["/snap/bin/firefox"], "/home/me");

        Assert.Equal(BrowserEngine.Gecko, browser.Engine);
        Assert.Equal("/home/me/snap/firefox/common/.mozilla/firefox", browser.DataPath);
    }

    [Fact]
    public void Fingerprint_leaves_unknown_browsers_alone()
    {
        var browser = new Browser("x", "x", "x");
        LinuxBrowserDiscovery.Fingerprint(browser, "qutebrowser.desktop", ["qutebrowser"], "/home/me");

        Assert.Equal(BrowserEngine.Unknown, browser.Engine);
        Assert.Equal(string.Empty, browser.DataPath);
    }

    [Theory]
    [InlineData(new[] { "flatpak", "run", "--branch=stable", "org.mozilla.firefox" }, "org.mozilla.firefox")]
    [InlineData(new[] { "/usr/bin/flatpak", "run", "com.brave.Browser", "--incognito" }, "com.brave.Browser")]
    [InlineData(new[] { "firefox" }, null)]
    public void GetFlatpakAppId_parses_run_commands(string[] command, string? expected) =>
        Assert.Equal(expected, LinuxBrowserDiscovery.GetFlatpakAppId(command));

    [Theory]
    [InlineData("/nix/store/abc-firefox-130/bin/firefox", true, "firefox")]
    [InlineData("/nix/store/abc-firefox-130/bin/firefox", false, "/nix/store/abc-firefox-130/bin/firefox")]
    [InlineData("/usr/bin/firefox", true, "/usr/bin/firefox")]
    public void StabilizeNixStorePath_prefers_path_name(string exe, bool onPath, string expected) =>
        Assert.Equal(expected, LinuxBrowserDiscovery.StabilizeNixStorePath(exe, _ => onPath ? "/run/current-system/sw/bin/x" : null));
}

public class LinuxBrowserRegistrationTests
{
    [Fact]
    public void Desktop_file_declares_browser_and_schemes()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string content = LinuxBrowserRegistration.BuildDesktopFile("/home/me/My Apps/bw.AppImage", "browser-wrangler");
        DesktopEntry entry = DesktopEntry.Parse("browser-wrangler.desktop", "/x", content)!;

        Assert.Equal(["/home/me/My Apps/bw.AppImage"], DesktopEntry.SplitExec(entry.Exec));
        Assert.EndsWith(" %u", entry.Exec);
        Assert.Contains("WebBrowser", entry.Get("Categories"));
        Assert.Equal(AppInfo.LinuxAppId, entry.Get("StartupWMClass"));
        Assert.Equal(["x-scheme-handler/http", "x-scheme-handler/https", "x-scheme-handler/x-bw"], entry.MimeTypes);
    }

    [Fact]
    public void MakeDefault_and_unregister_round_trip_in_isolated_dirs()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var fx = new XdgFixture();
        fx.Write("config/mimeapps.list", "[Default Applications]\nx-scheme-handler/http=firefox.desktop;\n");
        fx.Write("sys/applications/firefox.desktop", "[Desktop Entry]\nType=Application\nName=Firefox\nExec=firefox %u\n");
        var registration = new LinuxBrowserRegistration(fx.Dirs);

        registration.MakeDefaultBrowser();

        Assert.True(File.Exists(registration.DesktopFilePath));
        Assert.True(registration.IsRegisteredAsBrowser(out string error), error);
        Assert.True(registration.IsDefaultBrowser(out _, out _));

        registration.Unregister();

        Assert.False(File.Exists(registration.DesktopFilePath));
        Assert.Equal("firefox.desktop", registration.GetDefaultHandler("x-scheme-handler/http"));
        Assert.False(registration.IsDefaultBrowser(out _, out _));
    }
}

public class PickerHotkeysTests
{
    private static readonly PickerHotkeySupport Full = new(ModifierCombos: true, CapsLock: true);
    private static readonly PickerHotkeySupport CapsOnly = new(ModifierCombos: false, CapsLock: true);

    [Fact]
    public void Combos_trigger_when_enabled_and_supported()
    {
        var settings = new PickerSettings { OnCtrlShift = true, OnCapsLock = false };
        var keys = new KeyboardState(Ctrl: true, Shift: true, Alt: false, CapsLock: false);

        Assert.True(PickerHotkeys.IsTriggered(settings, keys, Full));
        Assert.False(PickerHotkeys.IsTriggered(settings, keys, CapsOnly));
        Assert.False(PickerHotkeys.IsTriggered(new PickerSettings { OnCtrlShift = false }, keys, Full));
    }

    [Fact]
    public void CapsLock_triggers_when_enabled_and_supported()
    {
        var keys = new KeyboardState(false, false, false, CapsLock: true);

        Assert.True(PickerHotkeys.IsTriggered(new PickerSettings { OnCapsLock = true }, keys, CapsOnly));
        Assert.False(PickerHotkeys.IsTriggered(new PickerSettings { OnCapsLock = true }, keys, new(true, false)));
        Assert.False(PickerHotkeys.IsTriggered(new PickerSettings { OnCapsLock = false }, keys, Full));
    }
}

internal sealed class XdgFixture : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bw-xdg-" + Guid.NewGuid().ToString("N"));

    public XdgFixture(params string[] desktops)
    {
        Directory.CreateDirectory(_root);
        Dirs = new XdgDirectories(
            Home,
            Path.Combine(_root, "data"),
            [Path.Combine(_root, "sys")],
            Path.Combine(_root, "config"),
            [Path.Combine(_root, "etc")],
            desktops);
    }

    public string Home => Path.Combine(_root, "home");

    public XdgDirectories Dirs { get; }

    public void Write(string relative, string content)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
