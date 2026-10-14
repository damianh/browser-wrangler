using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using BrowserWrangler.Core.Configuration;
using BrowserWrangler.Core.Discovery;
using BrowserWrangler.Core.Models;
using BrowserWrangler.Core.Setup;

namespace BrowserWrangler.Core.Platform.Windows;

/// <summary>Windows: registry-based discovery/registration and GetAsyncKeyState hotkeys.</summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsPlatform : IPlatform
{
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12; // Alt
    private const int VK_CAPITAL = 0x14;

    public bool CanSetDefaultBrowserDirectly => false;

    public PickerHotkeySupport PickerHotkeySupport => PickerHotkeySupport.All;

    public List<Browser> DiscoverBrowsers(bool includeFirefoxClassicProfiles = true) =>
        BrowserDiscovery.DiscoverBrowsers(AppInfo.ProgId, includeFirefoxClassicProfiles);

    public void Register() => BrowserRegistration.RegisterAll();

    public void Unregister() => BrowserRegistration.UnregisterAll();

    public bool EnsureRegistered() => BrowserRegistration.EnsureRegistered();

    public bool IsRegisteredAsBrowser(out string error) => BrowserRegistration.IsRegisteredAsBrowser(out error);

    public bool IsDefaultBrowser(out bool http, out bool https) =>
        BrowserRegistration.IsDefaultBrowser(out http, out https);

    public void MakeDefaultBrowser() => BrowserRegistration.OpenDefaultAppsSettings();

    public List<SystemCheck> GetChecks() => BrowserRegistration.GetChecks();

    public bool IsPickerHotkeyDown(PickerSettings settings)
    {
        static bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

        var keys = new KeyboardState(
            Ctrl: Down(VK_CONTROL),
            Shift: Down(VK_SHIFT),
            Alt: Down(VK_MENU),
            CapsLock: (GetAsyncKeyState(VK_CAPITAL) & 0x0001) != 0);

        return PickerHotkeys.IsTriggered(settings, keys, PickerHotkeySupport);
    }

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int vKey);
}
