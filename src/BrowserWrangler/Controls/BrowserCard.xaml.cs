using BrowserWrangler.Core.Models;
using BrowserWrangler.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BrowserWrangler.Controls;

/// <summary>
/// Collapsible card for one browser: header with icon, name, exe path and a
/// show/hide toggle; body with the browser's profiles.
/// </summary>
public sealed partial class BrowserCard : UserControl
{
    private Browser? _browser;

    public BrowserCard()
    {
        InitializeComponent();
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (args.NewValue is not Browser browser || ReferenceEquals(browser, _browser))
        {
            return;
        }

        _browser = browser;
        Build(browser);
    }

    private void Build(Browser browser)
    {
        var profileToggles = new List<ToggleSwitch>();

        // header row: icon | name + path | (spacer) | toggle
        var header = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 8, 8, 8) };
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

        var nameAndPath = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        nameAndPath.Children.Add(new TextBlock
        {
            Text = browser.Name,
            Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"],
        });
        nameAndPath.Children.Add(new TextBlock
        {
            Text = browser.OpenCommand,
            FontSize = 11,
            Opacity = 0.6,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(nameAndPath, 1);
        header.Children.Add(nameAndPath);

        var hide = new ToggleSwitch
        {
            IsOn = !browser.IsHidden,
            OnContent = null,
            OffContent = null,
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(hide, "Show this browser in pickers and dropdowns");
        hide.Toggled += (_, _) =>
        {
            AppState.MutateAndSave(_ => browser.IsHidden = !hide.IsOn);
            foreach (ToggleSwitch t in profileToggles)
            {
                t.IsEnabled = hide.IsOn;
            }
        };
        Grid.SetColumn(hide, 2);
        header.Children.Add(hide);
        Card.Header = header;

        var body = new StackPanel { Spacing = 4 };
        foreach (BrowserProfile profile in browser.Profiles)
        {
            // profile row: icon | name | rules | (spacer) | toggle
            var row = new Grid { ColumnSpacing = 8 };
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
                AppState.MutateAndSave(_ => profile.IsHidden = !profileHide.IsOn);
            };
            profileToggles.Add(profileHide);
            Grid.SetColumn(profileHide, 3);
            row.Children.Add(profileHide);
            body.Children.Add(row);
        }

        Card.Content = body;
    }
}
