using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using BrowserWrangler.Core.Platform;
using BrowserWrangler.Core.Setup;

namespace BrowserWrangler.Linux.Pages;

internal sealed class HealthPage : Page
{
    private readonly StackPanel _checks = new() { Spacing = 8 };

    public HealthPage()
        : base("Health")
    {
        IPlatform platform = PlatformServices.Current;
        Body.Children.Add(new WrapPanel
        {
            ItemSpacing = 8,
            LineSpacing = 8,
            Children =
            {
                Ui.Button("Register as browser", () => Run(platform.Register)),
                Ui.Button("Unregister", () => Run(platform.Unregister)),
                Ui.Button("Make default browser", () => Run(() =>
                {
                    platform.Register();
                    platform.MakeDefaultBrowser();
                }), accent: true),
                Ui.Button("Re-check", Build),
            },
        });
        Body.Children.Add(_checks);
        Build();
    }

    private void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _checks.Children.Insert(0, Ui.Text($"\u26a0 {ex.Message}"));
            return;
        }

        Build();
    }

    private void Build()
    {
        _checks.Children.Clear();
        foreach (SystemCheck check in PlatformServices.Current.GetChecks())
        {
            check.Recheck();
            var status = Ui.Text(check.IsOk ? "\u2714" : "\u2716", 18);
            status.Foreground = check.IsOk ? Brushes.SeaGreen : Brushes.IndianRed;

            var text = Ui.Column(2, Ui.Subheading(check.Name), Ui.Caption(check.Description));
            if (!check.IsOk && check.ErrorMessage.Length > 0)
            {
                text.Children.Add(Ui.Caption(check.ErrorMessage));
            }

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12 };
            grid.Children.Add(status);
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            if (!check.IsOk)
            {
                Button fix = Ui.Button("Fix", () => Run(check.Fix));
                ToolTip.SetTip(fix, check.FixDescription);
                Grid.SetColumn(fix, 2);
                grid.Children.Add(fix);
            }

            _checks.Children.Add(Ui.Card(grid));
        }
    }
}
