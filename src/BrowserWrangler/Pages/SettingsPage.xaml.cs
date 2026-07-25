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
        SafelinksEnabled.IsOn = AppState.Config.Pipeline.UnwrapSafelinks;
        ExpandShortLinksEnabled.IsOn = AppState.Config.Pipeline.ExpandShortenedUrls;
        LogRuleHitsEnabled.IsOn = AppState.Config.LogRuleHits;
        AutoCheckUpdatesEnabled.IsOn = AppState.Config.Updates.AutoCheckEnabled;
        UpdateIntervalHours.Value = AppState.Config.Updates.CheckIntervalHours;
        AutoDownloadInstallerEnabled.IsOn = AppState.Config.Updates.AutoDownloadInstaller;
        _loading = false;
    }

    private void PickerMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        int mode = PickerModeGroup.SelectedIndex;
        AppState.MutateAndSave(config =>
        {
            PickerSettings p = config.Picker;
            p.Always = mode == 3;
            p.OnConflict = mode is 1 or 2;
            p.OnNoRule = mode == 2;
        });
    }

    private void Setting_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        bool oldAutoCheckEnabled = false;
        bool newAutoCheckEnabled = false;
        AppState.MutateAndSave(config =>
        {
            oldAutoCheckEnabled = config.Updates.AutoCheckEnabled;
            PickerSettings p = config.Picker;
            p.OnCtrlShift = PickerCtrlShift.IsChecked == true;
            p.OnCtrlAlt = PickerCtrlAlt.IsChecked == true;
            p.OnAltShift = PickerAltShift.IsChecked == true;
            p.OnCapsLock = PickerCapsLock.IsChecked == true;
            p.CloseOnFocusLoss = PickerCloseOnFocusLoss.IsOn;
            config.Toast.ShowOnOpen = ToastEnabled.IsOn;
            config.Pipeline.UnwrapSafelinks = SafelinksEnabled.IsOn;
            config.Pipeline.ExpandShortenedUrls = ExpandShortLinksEnabled.IsOn;
            config.LogRuleHits = LogRuleHitsEnabled.IsOn;
            config.Updates.AutoCheckEnabled = AutoCheckUpdatesEnabled.IsOn;
            config.Updates.AutoDownloadInstaller = AutoDownloadInstallerEnabled.IsOn;
            newAutoCheckEnabled = config.Updates.AutoCheckEnabled;
        });

        if (oldAutoCheckEnabled != newAutoCheckEnabled)
        {
            AppState.Updates.NotifyScheduleChanged();
        }
    }

    private void Slider_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        int oldInterval = 0;
        int newInterval = 0;
        AppState.MutateAndSave(config =>
        {
            oldInterval = config.Updates.CheckIntervalHours;
            config.Toast.VisibleSeconds = (int)ToastDuration.Value;
            config.Updates.CheckIntervalHours = Math.Clamp((int)UpdateIntervalHours.Value, 1, 168);
            newInterval = config.Updates.CheckIntervalHours;
        });

        if (oldInterval != newInterval)
        {
            AppState.Updates.NotifyScheduleChanged();
        }
    }
}
