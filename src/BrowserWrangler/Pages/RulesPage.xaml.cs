using System.Collections.ObjectModel;
using BrowserWrangler.Core.Models;
using BrowserWrangler.Core.Rules;
using BrowserWrangler.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace BrowserWrangler.Pages;

/// <summary>
/// Flat, drag-orderable rules list. Every rule routes to a browser profile;
/// row order defines precedence (top wins). Order is persisted as bt-compatible
/// priorities, so the config file still round-trips with Browser Tamer semantics.
/// </summary>
public sealed partial class RulesPage : Page
{
    private sealed record RuleEntry(MatchRule Rule)
    {
        public BrowserProfile Profile { get; set; } = null!;
    }

    private readonly List<BrowserProfile> _profiles;
    private readonly ObservableCollection<Border> _rows = [];
    private bool _loading;

    public RulesPage()
    {
        InitializeComponent();
        _profiles = RuleMatcher.ToProfiles(AppState.Config.Browsers, skipHidden: false);
        RuleListView.ItemsSource = _rows;
        RuleListView.DragItemsCompleted += (_, _) => PersistOrder();
        BuildRows();
    }

    private void BuildRows()
    {
        _loading = true;
        _rows.Clear();

        var entries = _profiles
            .SelectMany(p => p.Rules.Select(r => new RuleEntry(r) { Profile = p }))
            .OrderByDescending(e => e.Rule.Priority)
            .ToList();

        foreach (RuleEntry entry in entries)
        {
            _rows.Add(BuildRow(entry));
        }

        _loading = false;
    }

    /// <summary>Writes row order back as descending priorities and saves.</summary>
    private void PersistOrder()
    {
        if (_loading)
        {
            return;
        }

        int priority = _rows.Count;
        foreach (Border row in _rows)
        {
            ((RuleEntry)row.Tag).Rule.Priority = priority--;
        }

        AppState.Save();
    }

    private Border BuildRow(RuleEntry entry)
    {
        var grid = new Grid { ColumnSpacing = 8, Padding = new Thickness(12) };
        foreach (int col in Enumerable.Range(0, 7))
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = col == 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto,
            });
        }

        // drag handle
        var handle = new FontIcon
        {
            Glyph = "\uE76F",
            FontSize = 14,
            Opacity = 0.5,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(handle, "Drag to reorder \u2014 topmost matching rule wins");
        grid.Children.Add(handle);

        // location: URL / Title / Process
        var location = new ComboBox { Width = 110, VerticalAlignment = VerticalAlignment.Center };
        location.Items.Add("URL");
        location.Items.Add("Title");
        location.Items.Add("Process");
        location.SelectedIndex = (int)entry.Rule.Location;
        Grid.SetColumn(location, 1);
        grid.Children.Add(location);

        // value
        var value = new TextBox
        {
            Text = entry.Rule.Value,
            PlaceholderText = "text or pattern to match",
            VerticalAlignment = VerticalAlignment.Center,
        };
        value.LostFocus += (_, _) =>
        {
            if (entry.Rule.Value != value.Text)
            {
                entry.Rule.Value = value.Text;
                Save();
            }
        };
        Grid.SetColumn(value, 2);
        grid.Children.Add(value);

        // scope (URL only): icon-only dropdown, expands to icon + text
        (string Glyph, string Label)[] scopes =
        [
            ("\uE71B", "Anywhere in URL"),
            ("\uE774", "Domain only"),
            ("\uED25", "Path only"),
        ];
        var scopeIcon = new FontIcon { FontSize = 14 };
        var scope = new DropDownButton
        {
            Content = scopeIcon,
            Width = 64,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var scopeFlyout = new MenuFlyout { Placement = FlyoutPlacementMode.Bottom };
        void SetScope(int index, bool save)
        {
            scopeIcon.Glyph = scopes[index].Glyph;
            ToolTipService.SetToolTip(scope, $"Match scope: {scopes[index].Label}");
            if (save)
            {
                entry.Rule.Scope = (MatchScope)index;
                Save();
            }
        }

        foreach (int i in Enumerable.Range(0, scopes.Length))
        {
            var item = new MenuFlyoutItem
            {
                Text = scopes[i].Label,
                Icon = new FontIcon { Glyph = scopes[i].Glyph },
            };
            item.Click += (_, _) => SetScope(i, save: true);
            scopeFlyout.Items.Add(item);
        }

        scope.Flyout = scopeFlyout;
        SetScope((int)entry.Rule.Scope, save: false);
        scope.IsEnabled = entry.Rule.Location == MatchLocation.Url;
        location.SelectionChanged += (_, _) =>
        {
            entry.Rule.Location = (MatchLocation)location.SelectedIndex;
            scope.IsEnabled = location.SelectedIndex == 0;
            Save();
        };
        Grid.SetColumn(scope, 3);
        grid.Children.Add(scope);

        // target browser/profile
        var target = new ComboBox { Width = 200, VerticalAlignment = VerticalAlignment.Center };
        foreach (BrowserProfile profile in _profiles)
        {
            var itemPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            if (IconLoader.GetIconForProfile(profile) is { } icon)
            {
                itemPanel.Children.Add(new Image
                {
                    Source = icon,
                    Width = 16,
                    Height = 16,
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }

            itemPanel.Children.Add(new TextBlock
            {
                Text = profile.BestDisplayName,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            target.Items.Add(itemPanel);
        }

        target.SelectedIndex = _profiles.IndexOf(entry.Profile);
        ToolTipService.SetToolTip(target, "Open matching links in this browser profile");
        target.SelectionChanged += (_, _) =>
        {
            if (target.SelectedIndex < 0 || _profiles[target.SelectedIndex] == entry.Profile)
            {
                return;
            }

            entry.Profile.Rules.Remove(entry.Rule);
            entry.Profile = _profiles[target.SelectedIndex];
            entry.Profile.Rules.Add(entry.Rule);
            Save();
        };
        Grid.SetColumn(target, 4);
        grid.Children.Add(target);

        // regex + app mode
        var toggles = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var regex = new ToggleButton { Content = ".*", IsChecked = entry.Rule.IsRegex, Padding = new Thickness(8, 4, 8, 4) };
        ToolTipService.SetToolTip(regex, "Regular expression (must match the whole input)");
        regex.Click += (_, _) =>
        {
            entry.Rule.IsRegex = regex.IsChecked == true;
            Save();
        };
        toggles.Children.Add(regex);
        var appMode = new ToggleButton { IsChecked = entry.Rule.AppMode, Padding = new Thickness(8, 4, 8, 4) };
        appMode.Content = new FontIcon { Glyph = "\uE737", FontSize = 14 };
        ToolTipService.SetToolTip(appMode, "Open in app mode (frameless window, Chromium only)");
        appMode.Click += (_, _) =>
        {
            entry.Rule.AppMode = appMode.IsChecked == true;
            Save();
        };
        toggles.Children.Add(appMode);
        Grid.SetColumn(toggles, 5);
        grid.Children.Add(toggles);

        // delete
        var delete = new Button { Padding = new Thickness(8, 6, 8, 6), VerticalAlignment = VerticalAlignment.Center };
        delete.Content = new FontIcon { Glyph = "\uE74D", FontSize = 14 };
        ToolTipService.SetToolTip(delete, "Delete rule");
        var row = new Border
        {
            Child = grid,
            CornerRadius = new CornerRadius(6),
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
        };
        delete.Click += (_, _) =>
        {
            entry.Profile.Rules.Remove(entry.Rule);
            _rows.Remove(row);
            PersistOrder();
        };
        Grid.SetColumn(delete, 6);
        grid.Children.Add(delete);

        row.Tag = entry;
        return row;
    }

    private void Save()
    {
        if (!_loading)
        {
            AppState.Save();
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (_profiles.Count == 0)
        {
            return;
        }

        var entry = new RuleEntry(new MatchRule()) { Profile = _profiles[0] };
        entry.Profile.Rules.Add(entry.Rule);
        _rows.Insert(0, BuildRow(entry));
        PersistOrder();
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        string url = TestUrlBox.Text.Trim();
        if (url.Length == 0)
        {
            return;
        }

        var matches = RuleMatcher.Match(
            AppState.Config.Browsers, new ClickPayload(url), AppState.Config.DefaultProfile);

        string text = matches.Count == 0
            ? "No browsers configured."
            : string.Join("\n", matches.Select((m, i) =>
                $"{i + 1}. {m.Profile.BestDisplayName}  \u2190  {(m.Rule.IsFallback ? "(default fallback)" : m.Rule.ToLine())}"));

        if (matches.Count > 1)
        {
            text += "\n\nMultiple rules match \u2014 the picker will show (if enabled), or the top rule wins.";
        }

        var dialog = new ContentDialog
        {
            Title = "Match result",
            Content = text,
            CloseButtonText = "Close",
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
    }
}
