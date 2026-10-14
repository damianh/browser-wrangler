using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using BrowserWrangler.Core.Discovery;
using BrowserWrangler.Core.Models;
using BrowserWrangler.Core.Rules;
using BrowserWrangler.Linux.Services;

namespace BrowserWrangler.Linux.Pages;

internal sealed class BrowsersPage : Page
{
    public BrowsersPage()
        : base("Browsers")
    {
        AddHeaderAction(Ui.Button("Rediscover", () =>
        {
            AppState.TryRefreshBrowsers();
            Build();
        }));
        Build();
    }

    private void Build()
    {
        Body.Children.Clear();

        var combo = new ComboBox { MinWidth = 280 };
        foreach (BrowserProfile profile in RuleMatcher.ToProfiles(AppState.Config.Browsers, skipHidden: false))
        {
            combo.Items.Add(new ComboBoxItem
            {
                Content = Ui.Row(8, Ui.Icon(profile.GetBestIconPath(), profile.Browser.Name, 16), Ui.Text(profile.BestDisplayName)),
                Tag = profile.LongId,
            });
            if (profile.LongId == AppState.Config.DefaultProfile)
            {
                combo.SelectedIndex = combo.ItemCount - 1;
            }
        }

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: string longId })
            {
                AppState.MutateAndSave(c => c.DefaultProfile = longId);
            }
        };
        Body.Children.Add(Ui.Row(12, Ui.Text("Default profile:"), combo));

        List<Browser> browsers = AppState.Config.Browsers;
        if (browsers.Count == 0)
        {
            Body.Children.Add(Ui.Caption("No browsers found. Install a browser and click Rediscover."));
        }

        for (int i = 0; i < browsers.Count; i++)
        {
            Body.Children.Add(BrowserCard(browsers[i], i, browsers.Count));
        }
    }

    private Control BrowserCard(Browser browser, int index, int count)
    {
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 12 };
        header.Children.Add(Ui.Icon(browser.BestIconPath, browser.Name, 32));
        var title = Ui.Column(2, Ui.Subheading(browser.Name), Ui.Caption(browser.OpenCommand));
        Grid.SetColumn(title, 1);
        header.Children.Add(title);

        var move = Ui.Row(
            4,
            MoveButton("\u25b2", "Move up", index > 0, () => Move(index, -1)),
            MoveButton("\u25bc", "Move down", index < count - 1, () => Move(index, 1)));
        Grid.SetColumn(move, 2);
        header.Children.Add(move);

        var show = Ui.Toggle(!browser.IsHidden, on =>
        {
            AppState.MutateAndSave(_ => browser.IsHidden = !on);
            Build();
        }, "Shown", "Hidden");
        ToolTip.SetTip(show, "Show this browser in the picker");
        Grid.SetColumn(show, 3);
        header.Children.Add(show);

        var column = Ui.Column(8, header);
        if (browser.Profiles.Count > 1 || browser.IsWellKnown)
        {
            foreach (BrowserProfile profile in browser.Profiles)
            {
                var row = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
                    ColumnSpacing = 12,
                    Margin = new Thickness(44, 0, 0, 0),
                };
                row.Children.Add(Ui.Icon(profile.GetBestIconPath(), profile.Name, 20));
                var name = Ui.Text(profile.IsIncognito ? $"{profile.Name} (private)" : profile.Name);
                Grid.SetColumn(name, 1);
                row.Children.Add(name);
                var rules = Ui.Caption(profile.Rules.Count == 1 ? "1 rule" : $"{profile.Rules.Count} rules");
                Grid.SetColumn(rules, 2);
                row.Children.Add(rules);
                var profileShow = Ui.Toggle(!profile.IsHidden, on => AppState.MutateAndSave(_ => profile.IsHidden = !on), "Shown", "Hidden");
                profileShow.IsEnabled = !browser.IsHidden;
                Grid.SetColumn(profileShow, 3);
                row.Children.Add(profileShow);
                column.Children.Add(row);
            }
        }

        return Ui.Card(column);
    }

    private static Button MoveButton(string glyph, string tip, bool enabled, Action onClick)
    {
        Button button = Ui.Button(glyph, onClick);
        button.IsEnabled = enabled;
        button.Padding = new Thickness(8, 4);
        ToolTip.SetTip(button, tip);
        return button;
    }

    private void Move(int index, int delta)
    {
        AppState.MutateAndSave(config =>
        {
            List<Browser> list = config.Browsers;
            (list[index], list[index + delta]) = (list[index + delta], list[index]);
            for (int i = 0; i < list.Count; i++)
            {
                list[i].SortOrder = i;
            }
        });
        Build();
    }
}
