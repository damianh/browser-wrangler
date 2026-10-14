using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using BrowserWrangler.Core.Updates;
using BrowserWrangler.Linux.Services;
using BrowserWrangler.Linux.Windows;

namespace BrowserWrangler.Linux.Pages;

internal sealed class AboutPage : Page
{
    private const string RepoUrl = "https://github.com/damianh/browser-wrangler";
    private const string NewIssueUrl = RepoUrl + "/issues/new";

    private readonly TextBlock _updateStatus = Ui.Caption(string.Empty);

    public AboutPage()
        : base("About")
    {
        Body.Children.Add(Ui.Row(
            16,
            new Image { Source = IconCache.Logo, Width = 64, Height = 64 },
            Ui.Column(
                2,
                Ui.Subheading(Core.AppInfo.Name),
                Ui.Caption($"Version {DiagnosticsInfo.VersionDisplay}"),
                Ui.Caption(DiagnosticsInfo.Copyright),
                Ui.Text("Redirects URLs you click to the right browser and profile, based on rules you define.", 13))));

        var diag = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 16, RowSpacing = 4 };
        int row = 0;
        foreach (DiagnosticsEntry entry in DiagnosticsInfo.Entries)
        {
            diag.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            TextBlock label = Ui.Caption(entry.Label);
            Grid.SetRow(label, row);
            diag.Children.Add(label);
            var value = new SelectableTextBlock { Text = entry.Value, FontSize = 13, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            Grid.SetRow(value, row);
            Grid.SetColumn(value, 1);
            diag.Children.Add(value);
            row++;
        }

        Button copy = Ui.Button("Copy diagnostics", () => { });
        copy.Click += async (_, _) => await Ui.CopyToClipboardAsync(this, DiagnosticsInfo.ToPlainText());
        Button check = Ui.Button("Check for updates", () => { });
        check.Click += async (_, _) => await CheckForUpdatesAsync(check);

        Body.Children.Add(Ui.Card(Ui.Column(
            8,
            Ui.Subheading("App info"),
            Ui.Caption("Handy when reporting a problem. Your home folder is shown as ~, and the links you open are never included."),
            diag,
            Ui.Row(8, copy, check),
            _updateStatus)));

        Body.Children.Add(Ui.Card(Ui.Column(
            8,
            Ui.Subheading("Support"),
            new WrapPanel
            {
                ItemSpacing = 8,
                LineSpacing = 8,
                Children =
                {
                    Link("Report an issue", NewIssueUrl),
                    Link("Releases and changelog", RepoUrl + "/releases"),
                    Link("Source code", RepoUrl),
                },
            })));

        Body.Children.Add(Ui.Card(Ui.Column(
            8,
            Ui.Subheading("Attribution"),
            Ui.Text("Browser Wrangler is a .NET fork of Browser Tamer by aloneguid, licensed under the Apache License 2.0. Portions of the design and behaviour are derived from that project. The Linux version uses Avalonia UI.", 13),
            new WrapPanel
            {
                ItemSpacing = 8,
                Children =
                {
                    Link("Browser Tamer", "https://github.com/aloneguid/bt"),
                    Link("Apache License 2.0", "https://www.apache.org/licenses/LICENSE-2.0"),
                    Link("Avalonia UI", "https://avaloniaui.net"),
                },
            })));
    }

    private static Control Link(string text, string url)
    {
        var button = new HyperlinkButton { Content = text, NavigateUri = new Uri(url) };
        return button;
    }

    private async Task CheckForUpdatesAsync(Button button)
    {
        button.IsEnabled = false;
        _updateStatus.Text = "Checking\u2026";
        try
        {
            UpdateCheckResult result = await UpdateNotifier.CheckAsync();
            _updateStatus.Text = result.Message;
            if (TopLevel.GetTopLevel(this) is MainWindow window)
            {
                window.ShowUpdate(result);
            }
        }
        finally
        {
            button.IsEnabled = true;
        }
    }
}
