using BrowserWrangler.Services;
using BrowserWrangler.Core.Models;
using BrowserWrangler.Core.Rules;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BrowserWrangler.Pages;

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
            var itemPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            if (IconLoader.GetIconForProfile(profile) is { } comboIcon)
            {
                itemPanel.Children.Add(new Image
                {
                    Source = comboIcon,
                    Width = 16,
                    Height = 16,
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }

            itemPanel.Children.Add(new TextBlock
            {
                Text = profile.BestDisplayName,
                VerticalAlignment = VerticalAlignment.Center,
            });
            DefaultProfileCombo.Items.Add(new ComboBoxItem
            {
                Content = itemPanel,
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
            var profileToggles = new List<ToggleSwitch>();

            // header row: icon | name | (spacer) | toggle
            var header = new Grid { ColumnSpacing = 12 };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (IconLoader.GetIconForExe(browser.OpenCommand) is { } browserIcon)
            {
                header.Children.Add(new Image
                {
                    Source = browserIcon,
                    Width = 24,
                    Height = 24,
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }

            var name = new TextBlock
            {
                Text = browser.Name,
                Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(name, 1);
            header.Children.Add(name);
            var hide = new ToggleSwitch
            {
                IsOn = !browser.IsHidden,
                OnContent = null,
                OffContent = null,
                MinWidth = 0,
            };
            ToolTipService.SetToolTip(hide, "Show this browser in pickers and dropdowns");
            hide.Toggled += (_, _) =>
            {
                browser.IsHidden = !hide.IsOn;
                foreach (ToggleSwitch t in profileToggles)
                {
                    t.IsEnabled = hide.IsOn;
                }

                AppState.Save();
            };
            Grid.SetColumn(hide, 2);
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
                // profile row: icon | name | rules | (spacer) | toggle
                var row = new Grid { ColumnSpacing = 8, Margin = new Thickness(16, 0, 0, 0) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                if (IconLoader.GetIconForProfile(profile) is { } profileIcon)
                {
                    row.Children.Add(new Image
                    {
                        Source = profileIcon,
                        Width = 16,
                        Height = 16,
                        VerticalAlignment = VerticalAlignment.Center,
                    });
                }

                var profileName = new TextBlock { Text = profile.Name, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(profileName, 1);
                row.Children.Add(profileName);
                var rules = new TextBlock
                {
                    Text = profile.Rules.Count == 1 ? "1 rule" : $"{profile.Rules.Count} rules",
                    Opacity = 0.6,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(rules, 2);
                row.Children.Add(rules);
                var profileHide = new ToggleSwitch
                {
                    IsOn = !profile.IsHidden,
                    OnContent = null,
                    OffContent = null,
                    MinWidth = 0,
                    IsEnabled = !browser.IsHidden,
                };
                ToolTipService.SetToolTip(profileHide, "Show this profile in pickers and dropdowns");
                profileHide.Toggled += (_, _) =>
                {
                    profile.IsHidden = !profileHide.IsOn;
                    AppState.Save();
                };
                profileToggles.Add(profileHide);
                Grid.SetColumn(profileHide, 3);
                row.Children.Add(profileHide);
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
