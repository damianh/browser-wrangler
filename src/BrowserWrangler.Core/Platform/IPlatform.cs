using BrowserWrangler.Core.Configuration;
using BrowserWrangler.Core.Models;
using BrowserWrangler.Core.Setup;

namespace BrowserWrangler.Core.Platform;

/// <summary>Which picker hotkeys the current session can detect.</summary>
public sealed record PickerHotkeySupport(bool ModifierCombos, bool CapsLock)
{
    public static PickerHotkeySupport All { get; } = new(true, true);
}

/// <summary>
/// The OS-specific parts of Browser Wrangler: browser discovery, registering as a browser,
/// default-browser state and keyboard state at click time.
/// </summary>
public interface IPlatform
{
    /// <summary>Discovers installed browsers (excluding ourselves) with their profiles.</summary>
    List<Browser> DiscoverBrowsers(bool includeFirefoxClassicProfiles = true);

    /// <summary>Registers the app as a browser that can handle http/https links.</summary>
    void Register();

    void Unregister();

    /// <summary>Repairs a missing or stale registration. Never throws; returns true when repaired.</summary>
    bool EnsureRegistered();

    bool IsRegisteredAsBrowser(out string error);

    /// <summary>True when both http and https are routed to us.</summary>
    bool IsDefaultBrowser(out bool http, out bool https);

    /// <summary>
    /// True when <see cref="MakeDefaultBrowser"/> changes the default directly; false when it can
    /// only open the system settings page (Windows only lets the user change the default).
    /// </summary>
    bool CanSetDefaultBrowserDirectly { get; }

    /// <summary>Makes us the default browser, or opens the system page where the user can.</summary>
    void MakeDefaultBrowser();

    List<SystemCheck> GetChecks();

    PickerHotkeySupport PickerHotkeySupport { get; }

    /// <summary>True when one of the configured picker hotkeys is held right now.</summary>
    bool IsPickerHotkeyDown(PickerSettings settings);
}
