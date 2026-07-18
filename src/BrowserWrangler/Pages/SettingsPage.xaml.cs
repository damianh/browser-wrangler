using BrowserWrangler.Core.Configuration;
using BrowserWrangler.Services;
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

        // map flags to the closest single mode
        PickerModeGroup.SelectedIndex = p switch
        {
            { Always: true } => 3,
            { OnConflict: true, OnNoRule: true } => 2,
            { OnConflict: true } => 1,
            _ => 0,
        };

        PickerCtrlShift.IsChecked = p.OnCtrlShift;
        PickerCtrlAlt.IsChecked = p.OnCtrlAlt;
        PickerAltShift.IsChecked = p.OnAltShift;
        PickerCapsLock.IsChecked = p.OnCapsLock;
        PickerCloseOnFocusLoss.IsOn = p.CloseOnFocusLoss;
        ToastEnabled.IsOn = AppState.Config.Toast.ShowOnOpen;
        ToastDuration.Value = AppState.Config.Toast.VisibleSeconds;
        _loading = false;
    }

    private void PickerMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        PickerSettings p = AppState.Config.Picker;
        int mode = PickerModeGroup.SelectedIndex;
        p.Always = mode == 3;
        p.OnConflict = mode is 1 or 2;
        p.OnNoRule = mode == 2;
        AppState.Save();
    }

    private void Setting_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        PickerSettings p = AppState.Config.Picker;
        p.OnCtrlShift = PickerCtrlShift.IsChecked == true;
        p.OnCtrlAlt = PickerCtrlAlt.IsChecked == true;
        p.OnAltShift = PickerAltShift.IsChecked == true;
        p.OnCapsLock = PickerCapsLock.IsChecked == true;
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
