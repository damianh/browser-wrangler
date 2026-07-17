using BrowserWrangler.Services;
using BrowserWrangler.Core.Configuration;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace BrowserWrangler.Pages;

public sealed partial class SettingsPage : Page
{
    private bool _loading = true;

    public SettingsPage()
    {
        InitializeComponent();
        PickerSettings p = AppState.Config.Picker;
        PickerAlways.IsOn = p.Always;
        PickerOnConflict.IsOn = p.OnConflict;
        PickerOnNoRule.IsOn = p.OnNoRule;
        PickerCtrlShift.IsOn = p.OnCtrlShift;
        PickerCtrlAlt.IsOn = p.OnCtrlAlt;
        PickerAltShift.IsOn = p.OnAltShift;
        PickerCapsLock.IsOn = p.OnCapsLock;
        PickerCloseOnFocusLoss.IsOn = p.CloseOnFocusLoss;
        ToastEnabled.IsOn = AppState.Config.Toast.ShowOnOpen;
        ToastDuration.Value = AppState.Config.Toast.VisibleSeconds;
        _loading = false;
    }

    private void Setting_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        PickerSettings p = AppState.Config.Picker;
        p.Always = PickerAlways.IsOn;
        p.OnConflict = PickerOnConflict.IsOn;
        p.OnNoRule = PickerOnNoRule.IsOn;
        p.OnCtrlShift = PickerCtrlShift.IsOn;
        p.OnCtrlAlt = PickerCtrlAlt.IsOn;
        p.OnAltShift = PickerAltShift.IsOn;
        p.OnCapsLock = PickerCapsLock.IsOn;
        p.CloseOnFocusLoss = PickerCloseOnFocusLoss.IsOn;
        AppState.Config.Toast.ShowOnOpen = ToastEnabled.IsOn;
        AppState.Save();
    }

    private void Slider_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        AppState.Config.Toast.VisibleSeconds = (int)ToastDuration.Value;
        AppState.Save();
    }
}
