using BrowserWrangler.Core.Configuration;

namespace BrowserWrangler.Core.Platform;

/// <summary>Snapshot of the keyboard at click time.</summary>
public readonly record struct KeyboardState(bool Ctrl, bool Shift, bool Alt, bool CapsLock);

public static class PickerHotkeys
{
    /// <summary>Evaluates the configured hotkeys, ignoring ones the session cannot detect.</summary>
    public static bool IsTriggered(PickerSettings settings, KeyboardState keys, PickerHotkeySupport support)
    {
        bool combos = support.ModifierCombos && (
            (settings.OnCtrlShift && keys.Ctrl && keys.Shift)
            || (settings.OnCtrlAlt && keys.Ctrl && keys.Alt)
            || (settings.OnAltShift && keys.Alt && keys.Shift));

        return combos || (support.CapsLock && settings.OnCapsLock && keys.CapsLock);
    }
}
