using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using BrowserWrangler.Core.Configuration;
using BrowserWrangler.Core.Platform;
using BrowserWrangler.Linux.Services;

namespace BrowserWrangler.Linux.Pages;

internal sealed class SettingsPage : Page
{
    public SettingsPage()
        : base("Settings")
    {
        AppConfig config = AppState.Config;
        PickerSettings picker = config.Picker;

        // picker
        int mode = picker.Always ? 3 : picker.OnConflict && picker.OnNoRule ? 2 : picker.OnConflict ? 1 : 0;
        string[] modes = ["Never automatically", "When multiple rules match", "When multiple rules match, or none match", "Always, for every link"];
        var modePanel = Ui.Column(4);
        for (int i = 0; i < modes.Length; i++)
        {
            int value = i;
            var radio = new RadioButton { Content = modes[i], GroupName = "picker-mode", IsChecked = i == mode };
            radio.IsCheckedChanged += (_, _) =>
            {
                if (radio.IsChecked == true)
                {
                    AppState.MutateAndSave(c =>
                    {
                        c.Picker.Always = value == 3;
                        c.Picker.OnConflict = value is 1 or 2;
                        c.Picker.OnNoRule = value == 2;
                    });
                }
            };
            modePanel.Children.Add(radio);
        }

        PickerHotkeySupport support = PlatformServices.Current.PickerHotkeySupport;
        var hotkeys = Ui.Column(4);
        if (support.ModifierCombos)
        {
            hotkeys.Children.Add(Ui.Check("Ctrl + Shift + click", picker.OnCtrlShift, v => Set(c => c.Picker.OnCtrlShift = v)));
            hotkeys.Children.Add(Ui.Check("Ctrl + Alt + click", picker.OnCtrlAlt, v => Set(c => c.Picker.OnCtrlAlt = v)));
            hotkeys.Children.Add(Ui.Check("Alt + Shift + click", picker.OnAltShift, v => Set(c => c.Picker.OnAltShift = v)));
        }

        if (support.CapsLock)
        {
            hotkeys.Children.Add(Ui.Check("Caps Lock is on", picker.OnCapsLock, v => Set(c => c.Picker.OnCapsLock = v)));
        }

        Section(
            "Picker",
            Ui.Caption("The picker is a popup that lets you choose a browser for the link you just clicked."),
            Ui.Subheading("Show the picker"),
            modePanel,
            Ui.Subheading("Force with a hotkey"),
            Ui.Caption(support.ModifierCombos
                ? "Holding any of these while clicking a link always opens the picker, whatever the mode above."
                : "Modifier keys can't be read on Wayland, so only Caps Lock is available in this session."),
            hotkeys.Children.Count > 0 ? hotkeys : Ui.Caption("No hotkeys can be detected in this session."),
            Ui.Check("Show number key hints", picker.ShowKeyHints, v => Set(c => c.Picker.ShowKeyHints = v)),
            Ui.Check("Close the picker when it loses focus", picker.CloseOnFocusLoss, v => Set(c => c.Picker.CloseOnFocusLoss = v)));

        // appearance
        string[] themes = ["", "light", "dark"];
        var theme = new ComboBox
        {
            ItemsSource = new[] { "Use system setting", "Light", "Dark" },
            SelectedIndex = Math.Max(0, Array.IndexOf(themes, config.Theme)),
            MinWidth = 200,
        };
        theme.SelectionChanged += (_, _) =>
        {
            string value = themes[Math.Max(0, theme.SelectedIndex)];
            Set(c => c.Theme = value);
            App.ApplyTheme(value);
        };
        Section("Appearance", Ui.Row(12, Ui.Text("Theme"), theme));

        // url preprocessing
        Section(
            "URL preprocessing",
            Ui.Check("Decode Outlook Safelinks before matching rules", config.Pipeline.UnwrapSafelinks, v => Set(c => c.Pipeline.UnwrapSafelinks = v)),
            Ui.Caption("Turns Microsoft 365 Safelinks URLs back into their original destination before Browser Wrangler evaluates rules."),
            Ui.Check("Expand shortened links before matching rules", config.Pipeline.ExpandShortenedUrls, v => Set(c => c.Pipeline.ExpandShortenedUrls = v)),
            Ui.Caption("Follows HTTP redirects before matching so rules can see the final destination. This can add latency and shares clicked URLs with redirect services."));

        // toast
        var duration = new NumericUpDown { Minimum = 1, Maximum = 30, Value = config.Toast.VisibleSeconds, Increment = 1, FormatString = "0", MinWidth = 140 };
        duration.ValueChanged += (_, _) => Set(c => c.Toast.VisibleSeconds = (int)(duration.Value ?? 3));
        Section(
            "Toast",
            Ui.Check("Show toast when a URL is routed", config.Toast.ShowOnOpen, v => Set(c => c.Toast.ShowOnOpen = v)),
            Ui.Row(12, Ui.Text("Toast duration (seconds)"), duration));

        // log
        Section(
            "Rule-hit log",
            Ui.Check("Record rule hits for viewing later", config.LogRuleHits, v => Set(c => c.LogRuleHits = v)));

        // updates
        var interval = new NumericUpDown { Minimum = 1, Maximum = 168, Value = config.Updates.CheckIntervalHours, Increment = 1, FormatString = "0", MinWidth = 140 };
        interval.ValueChanged += (_, _) => Set(c => c.Updates.CheckIntervalHours = Math.Clamp((int)(interval.Value ?? 24), 1, 168));
        Section(
            "Updates",
            Ui.Check("Check for updates when this window opens", config.Updates.AutoCheckEnabled, v => Set(c => c.Updates.AutoCheckEnabled = v)),
            Ui.Row(12, Ui.Text("Check interval (hours)"), interval),
            Ui.Caption("Browser Wrangler only tells you about new versions; update through your package manager, or download the new AppImage or tarball."));
    }

    private static void Set(Action<AppConfig> mutator) => AppState.MutateAndSave(mutator);

    private void Section(string title, params Control[] children)
    {
        var column = Ui.Column(8, Ui.Subheading(title));
        column.Children.AddRange(children);
        Body.Children.Add(Ui.Card(column));
    }
}
