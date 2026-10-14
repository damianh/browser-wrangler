using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BrowserWrangler.Core.Platform;
using BrowserWrangler.Linux.Services;

namespace BrowserWrangler.Linux.Windows;

/// <summary>First-run window: register as a browser, then become the default.</summary>
internal sealed class WelcomeWindow : Window
{
    private readonly TextBlock _registeredStatus = new();
    private readonly TextBlock _defaultStatus = new();
    private readonly Button _registerButton;
    private readonly Button _defaultButton;
    private readonly DispatcherTimer _poll;

    public WelcomeWindow()
    {
        Title = $"Welcome to {Core.AppInfo.Name}";
        Icon = new WindowIcon(IconCache.Logo);
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _registerButton = Ui.Button("Register", () => Run(PlatformServices.Current.Register));
        _defaultButton = Ui.Button("Make default", () => Run(PlatformServices.Current.MakeDefaultBrowser), accent: true);

        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 16,
            Children =
            {
                Ui.Row(12, new Image { Source = IconCache.Logo, Width = 48, Height = 48 }, Ui.Heading($"Welcome to {Core.AppInfo.Name}")),
                Ui.Text("Two quick steps and your links will start going to the right browser and profile."),
                Step(_registeredStatus, "Registered as a browser",
                    "Browser Wrangler installs a .desktop entry so it shows up in your desktop's list of web browsers.",
                    _registerButton),
                Step(_defaultStatus, "Set as your default browser",
                    "Sets Browser Wrangler as the handler for HTTP and HTTPS links (mimeapps.list, plus KDE's default browser setting).",
                    _defaultButton),
                Ui.Caption("You can revisit these checks any time on the Health page."),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children =
                    {
                        Ui.Button("Open Browser Wrangler", () =>
                        {
                            App.CurrentApp?.ShowMainWindow();
                            Close();
                        }),
                        Ui.Button("Close", Close),
                    },
                },
            },
        };

        _poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _poll.Tick += (_, _) => Refresh();
        Opened += (_, _) =>
        {
            Refresh();
            _poll.Start();
        };
        Closed += (_, _) =>
        {
            _poll.Stop();
            AppState.MutateAndSave(c => c.SetupCompleted = true);
        };
    }

    private static Control Step(TextBlock status, string title, string description, Button action)
    {
        status.FontSize = 18;
        status.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12 };
        grid.Children.Add(status);
        var text = Ui.Column(2, Ui.Subheading(title), Ui.Caption(description));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        Grid.SetColumn(action, 2);
        grid.Children.Add(action);
        return Ui.Card(grid);
    }

    private void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _defaultStatus.Text = "\u26a0";
            ToolTip.SetTip(_defaultStatus, ex.Message);
        }

        Refresh();
    }

    private void Refresh()
    {
        IPlatform platform = PlatformServices.Current;
        bool registered = platform.IsRegisteredAsBrowser(out _);
        bool isDefault = platform.IsDefaultBrowser(out _, out _);
        SetStatus(_registeredStatus, registered);
        SetStatus(_defaultStatus, isDefault);
        _registerButton.IsEnabled = !registered;
        _defaultButton.IsEnabled = !isDefault;
    }

    private static void SetStatus(TextBlock block, bool ok)
    {
        block.Text = ok ? "\u2714" : "\u25cb";
        if (ok)
        {
            block.Foreground = Brushes.SeaGreen;
        }
        else
        {
            block.ClearValue(TextBlock.ForegroundProperty);
        }
    }
}
