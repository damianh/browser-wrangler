using BrowserWrangler.Core.Models;
using BrowserWrangler.Core.Rules;
using BrowserWrangler.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace BrowserWrangler.Pages;

/// <summary>
/// Structured per-rule editor (styled after bt): each rule is a row with a
/// location dropdown, value box, scope, priority, regex/app toggles and delete.
/// </summary>
public sealed partial class RulesPage : Page
{
    private readonly List<BrowserProfile> _profiles;
    private bool _loading;

    public RulesPage()
    {
        InitializeComponent();
        _profiles = RuleMatcher.ToProfiles(AppState.Config.Browsers, skipHidden: false);
        foreach (BrowserProfile profile in _profiles)
        {
            ProfileCombo.Items.Add(profile.BestDisplayName);
        }

        if (_profiles.Count > 0)
        {
            ProfileCombo.SelectedIndex = 0;
        }
    }

    private BrowserProfile? SelectedProfile =>
        ProfileCombo.SelectedIndex >= 0 && ProfileCombo.SelectedIndex < _profiles.Count
            ? _profiles[ProfileCombo.SelectedIndex]
            : null;

    private void Profile_Changed(object sender, SelectionChangedEventArgs e) => RebuildRows();

    private void RebuildRows()
    {
        _loading = true;
        RuleList.Children.Clear();
        if (SelectedProfile is { } profile)
        {
            foreach (MatchRule rule in profile.Rules)
            {
                RuleList.Children.Add(BuildRow(rule));
            }
        }

        _loading = false;
    }

    private UIElement BuildRow(MatchRule rule)
    {
        var grid = new Grid { ColumnSpacing = 8, Padding = new Thickness(12) };
        for (int i = 0; i < 6; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = i == 1 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto,
            });
        }

        // location: URL / Title / Process
        var location = new ComboBox { MinWidth = 110, VerticalAlignment = VerticalAlignment.Center };
        location.Items.Add("URL");
        location.Items.Add("Title");
        location.Items.Add("Process");
        location.SelectedIndex = (int)rule.Location;
        location.SelectionChanged += (_, _) =>
        {
            rule.Location = (MatchLocation)location.SelectedIndex;
            SaveRules();
        };
        grid.Children.Add(location);

        // value
        var value = new TextBox
        {
            Text = rule.Value,
            PlaceholderText = "text or pattern to match",
            VerticalAlignment = VerticalAlignment.Center,
        };
        value.LostFocus += (_, _) =>
        {
            if (rule.Value != value.Text)
            {
                rule.Value = value.Text;
                SaveRules();
            }
        };
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);

        // scope (URL only): whole URL / domain / path
        var scope = new ComboBox { MinWidth = 120, VerticalAlignment = VerticalAlignment.Center };
        scope.Items.Add("Whole URL");
        scope.Items.Add("Domain");
        scope.Items.Add("Path");
        scope.SelectedIndex = (int)rule.Scope;
        scope.IsEnabled = rule.Location == MatchLocation.Url;
        scope.SelectionChanged += (_, _) =>
        {
            rule.Scope = (MatchScope)scope.SelectedIndex;
            SaveRules();
        };
        location.SelectionChanged += (_, _) => scope.IsEnabled = location.SelectedIndex == 0;
        Grid.SetColumn(scope, 2);
        grid.Children.Add(scope);

        // priority
        var priority = new NumberBox
        {
            Value = rule.Priority,
            Minimum = 0,
            Maximum = 999,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            SmallChange = 1,
            MinWidth = 70,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(priority, "Priority — higher wins when multiple rules match");
        priority.ValueChanged += (_, _) =>
        {
            rule.Priority = double.IsNaN(priority.Value) ? 0 : (int)priority.Value;
            SaveRules();
        };
        Grid.SetColumn(priority, 3);
        grid.Children.Add(priority);

        // regex + app-mode toggles
        var toggles = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var regex = new ToggleButton { Content = ".*", IsChecked = rule.IsRegex, Padding = new Thickness(8, 4, 8, 4) };
        ToolTipService.SetToolTip(regex, "Regular expression (must match the whole input)");
        regex.Click += (_, _) =>
        {
            rule.IsRegex = regex.IsChecked == true;
            SaveRules();
        };
        toggles.Children.Add(regex);
        var appMode = new ToggleButton { IsChecked = rule.AppMode, Padding = new Thickness(8, 4, 8, 4) };
        appMode.Content = new FontIcon { Glyph = "\uE737", FontSize = 14 };
        ToolTipService.SetToolTip(appMode, "Open in app mode (frameless window, Chromium only)");
        appMode.Click += (_, _) =>
        {
            rule.AppMode = appMode.IsChecked == true;
            SaveRules();
        };
        toggles.Children.Add(appMode);
        Grid.SetColumn(toggles, 4);
        grid.Children.Add(toggles);

        // delete
        var delete = new Button { Padding = new Thickness(8, 6, 8, 6), VerticalAlignment = VerticalAlignment.Center };
        delete.Content = new FontIcon { Glyph = "\uE74D", FontSize = 14 };
        ToolTipService.SetToolTip(delete, "Delete rule");
        delete.Click += (_, _) =>
        {
            SelectedProfile?.Rules.Remove(rule);
            SaveRules();
            RebuildRows();
        };
        Grid.SetColumn(delete, 5);
        grid.Children.Add(delete);

        return new Border
        {
            Child = grid,
            CornerRadius = new CornerRadius(6),
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
        };
    }

    private void SaveRules()
    {
        if (!_loading)
        {
            AppState.Save();
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile is not { } profile)
        {
            return;
        }

        profile.Rules.Add(new MatchRule());
        RebuildRows();
    }

    private async void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile is not { } profile || profile.Rules.Count == 0)
        {
            return;
        }

        var confirm = new ContentDialog
        {
            Title = "Clear all rules?",
            Content = $"Delete all {profile.Rules.Count} rules for {profile.BestDisplayName}?",
            PrimaryButtonText = "Clear all",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await confirm.ShowAsync() == ContentDialogResult.Primary)
        {
            profile.Rules.Clear();
            SaveRules();
            RebuildRows();
        }
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
            : string.Join("\n", matches.Select(m =>
                $"{m.Profile.BestDisplayName}  \u2190  {(m.Rule.IsFallback ? "(default fallback)" : m.Rule.ToLine())}"));

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
