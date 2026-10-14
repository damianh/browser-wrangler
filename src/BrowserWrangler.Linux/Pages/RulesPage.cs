using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using BrowserWrangler.Core.Models;
using BrowserWrangler.Core.Rules;
using BrowserWrangler.Linux.Services;

namespace BrowserWrangler.Linux.Pages;

internal sealed class RulesPage : Page
{
    private sealed class RuleEntry(MatchRule rule, BrowserProfile profile)
    {
        public MatchRule Rule { get; } = rule;
        public BrowserProfile Profile { get; set; } = profile;
    }

    private readonly List<BrowserProfile> _profiles;
    private readonly List<RuleEntry> _entries;
    private readonly StackPanel _rows = new() { Spacing = 8 };
    private readonly TextBox _testBox = new() { PlaceholderText = "Paste a URL to see which rule matches", MinWidth = 360 };
    private readonly TextBlock _testResult = Ui.Text(string.Empty, 13);

    public RulesPage()
        : base("Rules")
    {
        _profiles = RuleMatcher.ToProfiles(AppState.Config.Browsers, skipHidden: false);
        _entries = _profiles
            .SelectMany(p => p.Rules.Select(r => new RuleEntry(r, p)))
            .OrderByDescending(e => e.Rule.Priority)
            .ToList();

        Button add = Ui.Button("Add rule", AddRule, accent: true);
        add.IsEnabled = _profiles.Count > 0;
        AddHeaderAction(add);

        Button test = Ui.Button("Test", Test);
        _testBox.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                Test();
            }
        };
        var testGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        testGrid.Children.Add(_testBox);
        Grid.SetColumn(test, 1);
        testGrid.Children.Add(test);
        Body.Children.Add(Ui.Card(Ui.Column(8, testGrid, _testResult)));
        Body.Children.Add(Ui.Caption("Rules are evaluated top to bottom. The first match wins unless the picker is set to show on conflicts."));
        Body.Children.Add(_rows);
        BuildRows();
    }

    private void BuildRows()
    {
        _rows.Children.Clear();
        if (_entries.Count == 0)
        {
            _rows.Children.Add(Ui.Caption(_profiles.Count == 0
                ? "No browsers found yet. Visit the Browsers page and click Rediscover."
                : "No rules yet. Click Add rule to send matching links to a specific browser profile."));
        }

        for (int i = 0; i < _entries.Count; i++)
        {
            _rows.Children.Add(BuildRow(_entries[i], i));
        }
    }

    private Control BuildRow(RuleEntry entry, int index)
    {
        MatchRule rule = entry.Rule;

        var value = new TextBox { Text = rule.Value, PlaceholderText = "e.g. github.com or .*\\.corp\\..*" };
        value.TextChanged += (_, _) => Save(() => rule.Value = value.Text ?? string.Empty);

        var location = new ComboBox { ItemsSource = new[] { "URL", "Window title", "Process name" }, SelectedIndex = (int)rule.Location, MinWidth = 130 };
        ToolTip.SetTip(location, "What the rule matches against");
        var scope = new ComboBox { ItemsSource = new[] { "Anywhere", "Domain", "Path" }, SelectedIndex = (int)rule.Scope, MinWidth = 110 };
        ToolTip.SetTip(scope, "Which part of the URL to match");
        scope.IsEnabled = rule.Location == MatchLocation.Url;
        location.SelectionChanged += (_, _) =>
        {
            Save(() => rule.Location = (MatchLocation)Math.Max(0, location.SelectedIndex));
            scope.IsEnabled = rule.Location == MatchLocation.Url;
        };
        scope.SelectionChanged += (_, _) => Save(() => rule.Scope = (MatchScope)Math.Max(0, scope.SelectedIndex));

        var target = new ComboBox { MinWidth = 220 };
        foreach (BrowserProfile profile in _profiles)
        {
            target.Items.Add(Ui.Row(8, Ui.Icon(profile.GetBestIconPath(), profile.Browser.Name, 16), Ui.Text(profile.BestDisplayName)));
        }

        target.SelectedIndex = _profiles.IndexOf(entry.Profile);
        ToolTip.SetTip(target, "Open matching links in this browser profile");
        target.SelectionChanged += (_, _) =>
        {
            if (target.SelectedIndex < 0 || _profiles[target.SelectedIndex] == entry.Profile)
            {
                return;
            }

            Save(() =>
            {
                entry.Profile.Rules.Remove(rule);
                entry.Profile = _profiles[target.SelectedIndex];
                entry.Profile.Rules.Add(rule);
            });
        };

        var regex = new ToggleButton { Content = ".*", IsChecked = rule.IsRegex };
        ToolTip.SetTip(regex, "Regular expression (must match the whole input)");
        regex.IsCheckedChanged += (_, _) => Save(() => rule.IsRegex = regex.IsChecked == true);

        var appMode = new ToggleButton { Content = "App", IsChecked = rule.AppMode };
        ToolTip.SetTip(appMode, "Open in app mode (frameless window, Chromium only)");
        appMode.IsCheckedChanged += (_, _) => Save(() => rule.AppMode = appMode.IsChecked == true);

        Button up = Small("\u25b2", "Move up", () => Move(index, -1));
        up.IsEnabled = index > 0;
        Button down = Small("\u25bc", "Move down", () => Move(index, 1));
        down.IsEnabled = index < _entries.Count - 1;
        Button delete = Small("\U0001F5D1", "Delete rule", () =>
        {
            entry.Profile.Rules.Remove(rule);
            _entries.Remove(entry);
            PersistOrder();
            BuildRows();
        });

        var controls = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8 };
        controls.Children.AddRange([location, scope, target, regex, appMode, up, down, delete]);

        return Ui.Card(Ui.Column(8, value, controls));
    }

    private static Button Small(string content, string tip, Action onClick)
    {
        Button button = Ui.Button(content, onClick);
        ToolTip.SetTip(button, tip);
        return button;
    }

    private void Move(int index, int delta)
    {
        (_entries[index], _entries[index + delta]) = (_entries[index + delta], _entries[index]);
        PersistOrder();
        BuildRows();
    }

    /// <summary>Writes row order back as descending priorities and saves.</summary>
    private void PersistOrder() =>
        Save(() =>
        {
            int priority = _entries.Count;
            foreach (RuleEntry entry in _entries)
            {
                entry.Rule.Priority = priority--;
            }
        });

    private static void Save(Action mutator) => AppState.MutateAndSave(_ => mutator());

    private void AddRule()
    {
        if (_profiles.Count == 0)
        {
            return;
        }

        var entry = new RuleEntry(new MatchRule(), _profiles[0]);
        entry.Profile.Rules.Add(entry.Rule);
        _entries.Insert(0, entry);
        PersistOrder();
        BuildRows();
        if (_rows.Children.FirstOrDefault() is Border { Child: StackPanel { Children: [TextBox box, ..] } })
        {
            box.Focus();
        }
    }

    private void Test()
    {
        string url = _testBox.Text?.Trim() ?? string.Empty;
        if (url.Length == 0)
        {
            _testResult.Text = string.Empty;
            return;
        }

        var matches = RuleMatcher.Match(AppState.Config.Browsers, new ClickPayload(url), AppState.Config.DefaultProfile);
        string text = matches.Count == 0
            ? "No browsers configured."
            : string.Join("\n", matches.Select((m, i) =>
                $"{i + 1}. {m.Profile.BestDisplayName}  \u2190  {(m.Rule.IsFallback ? "(default fallback)" : m.Rule.ToLine())}"));

        if (matches.Count > 1)
        {
            text += "\n\nMultiple rules match \u2014 the picker will show (if enabled), or the top rule wins.";
        }

        _testResult.Text = text;
    }
}
