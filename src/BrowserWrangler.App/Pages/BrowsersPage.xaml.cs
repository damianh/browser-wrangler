using BrowserWrangler.App.Services;
using BrowserWrangler.Core.Models;
using BrowserWrangler.Core.Rules;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BrowserWrangler.App.Pages;

public sealed partial class BrowsersPage : Page
{
    private bool _loading;

    public BrowsersPage()
    {
        InitializeComponent();
        if (AppState.Config.Browsers.Count == 0)
        {
            AppState.RefreshBrowsers();
        }

        Rebuild();
    }

    private void Rebuild()
    {
        _loading = true;
        BrowserList.Children.Clear();
        DefaultProfileCombo.Items.Clear();

        foreach (BrowserProfile profile in RuleMatcher.ToProfiles(AppState.Config.Browsers, skipHidden: false))
        {
            DefaultProfileCombo.Items.Add(new ComboBoxItem
            {
                Content = profile.BestDisplayName,
                Tag = profile.LongId,
            });
            if (profile.LongId == AppState.Config.DefaultProfile)
            {
                DefaultProfileCombo.SelectedIndex = DefaultProfileCombo.Items.Count - 1;
            }
        }

        foreach (Browser browser in AppState.Config.Browsers)
        {
            var panel = new StackPanel { Spacing = 4 };

            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            header.Children.Add(new TextBlock
            {
                Text = browser.Name,
                Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"],
                VerticalAlignment = VerticalAlignment.Center,
            });
            var hide = new ToggleSwitch
            {
                OnContent = "Hidden",
                OffContent = "Visible",
                IsOn = browser.IsHidden,
            };
            hide.Toggled += (_, _) =>
            {
                browser.IsHidden = hide.IsOn;
                AppState.Save();
            };
            header.Children.Add(hide);
            panel.Children.Add(header);
            panel.Children.Add(new TextBlock
            {
                Text = browser.OpenCommand,
                FontSize = 11,
                Opacity = 0.6,
            });

            foreach (BrowserProfile profile in browser.Profiles)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(16, 0, 0, 0) };
                row.Children.Add(new TextBlock { Text = profile.Name, VerticalAlignment = VerticalAlignment.Center, MinWidth = 180 });
                var profileHide = new CheckBox { Content = "Hide", IsChecked = profile.IsHidden };
                profileHide.Click += (_, _) =>
                {
                    profile.IsHidden = profileHide.IsChecked == true;
                    AppState.Save();
                };
                row.Children.Add(profileHide);
                row.Children.Add(new TextBlock
                {
                    Text = profile.Rules.Count == 1 ? "1 rule" : $"{profile.Rules.Count} rules",
                    Opacity = 0.6,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                panel.Children.Add(row);
            }

            BrowserList.Children.Add(new Border
            {
                Child = panel,
                Padding = new Thickness(12),
                CornerRadius = new CornerRadius(6),
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            });
        }

        _loading = false;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        AppState.RefreshBrowsers();
        Rebuild();
    }

    private void DefaultProfile_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        if (DefaultProfileCombo.SelectedItem is ComboBoxItem { Tag: string longId })
        {
            AppState.Config.DefaultProfile = longId;
            AppState.Save();
        }
    }
}
