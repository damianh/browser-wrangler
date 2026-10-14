using System.Runtime.Versioning;
using BrowserWrangler.Core.Configuration;
using BrowserWrangler.Core.Models;
using BrowserWrangler.Core.Setup;

namespace BrowserWrangler.Core.Platform.Linux;

[SupportedOSPlatform("linux")]
public sealed class LinuxPlatform : IPlatform
{
    private readonly XdgDirectories _dirs;
    private readonly LinuxBrowserRegistration _registration;
    private readonly Lazy<PickerHotkeySupport> _hotkeySupport =
        new(() => new PickerHotkeySupport(LinuxKeyboard.IsX11Session, LinuxKeyboard.HasCapsLockLed));

    public LinuxPlatform()
        : this(XdgDirectories.FromEnvironment())
    {
    }

    public LinuxPlatform(XdgDirectories dirs)
    {
        _dirs = dirs;
        _registration = new LinuxBrowserRegistration(dirs);
    }

    public bool CanSetDefaultBrowserDirectly => true;

    public PickerHotkeySupport PickerHotkeySupport => _hotkeySupport.Value;

    public List<Browser> DiscoverBrowsers(bool includeFirefoxClassicProfiles = true) =>
        LinuxBrowserDiscovery.DiscoverBrowsers(_dirs, includeFirefoxClassicProfiles);

    public void Register() => _registration.Register();

    public void Unregister() => _registration.Unregister();

    public bool EnsureRegistered() => _registration.EnsureRegistered();

    public bool IsRegisteredAsBrowser(out string error) => _registration.IsRegisteredAsBrowser(out error);

    public bool IsDefaultBrowser(out bool http, out bool https) => _registration.IsDefaultBrowser(out http, out https);

    public void MakeDefaultBrowser() => _registration.MakeDefaultBrowser();

    public List<SystemCheck> GetChecks() => _registration.GetChecks();

    public bool IsPickerHotkeyDown(PickerSettings settings)
    {
        PickerHotkeySupport support = PickerHotkeySupport;
        bool wantsCombos = support.ModifierCombos && (settings.OnCtrlShift || settings.OnCtrlAlt || settings.OnAltShift);
        (bool ctrl, bool shift, bool alt) = wantsCombos ? LinuxKeyboard.QueryX11Modifiers() : default;
        bool caps = support.CapsLock && settings.OnCapsLock && LinuxKeyboard.IsCapsLockOn();
        return PickerHotkeys.IsTriggered(settings, new KeyboardState(ctrl, shift, alt, caps), support);
    }
}
