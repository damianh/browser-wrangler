using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BrowserWrangler.Core.Configuration;
using BrowserWrangler.Core.Platform;
using BrowserWrangler.Core.Updates;
using BrowserWrangler.Linux.Pages;
using BrowserWrangler.Linux.Services;

namespace BrowserWrangler.Linux.Windows;

/// <summary>Config window: navigation on the left, the selected page on the right.</summary>
internal sealed class MainWindow : Window
{
    private static readonly (string Name, Func<Control> Create)[] PageFactories =
    [
        ("Browsers", () => new BrowsersPage()),
        ("Rules", () => new RulesPage()),
        ("Rule hits", () => new RuleHitsPage()),
        ("Settings", () => new SettingsPage()),
        ("Health", () => new HealthPage()),
        ("About", () => new AboutPage()),
    ];

    private readonly ContentControl _content = new() { Margin = new Thickness(24, 16) };
    private readonly Border _defaultBar;
    private readonly Border _updateBar;
    private readonly TextBlock _updateText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly ListBox _nav;
    private string? _updateUrl;

    public MainWindow()
    {
        Title = Core.AppInfo.Name;
        Icon = new WindowIcon(IconCache.Logo);
        MinWidth = 720;
        MinHeight = 480;

        WindowSettings saved = AppState.Config.Window;
        Width = saved.Width >= 720 ? saved.Width : 1000;
        Height = saved.Height >= 480 ? saved.Height : 720;
        if (saved.X != int.MinValue && saved.Y != int.MinValue)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(saved.X, saved.Y);
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        _nav = new ListBox { Width = 180, Margin = new Thickness(8), Background = Brushes.Transparent };
        foreach ((string name, _) in PageFactories)
        {
            _nav.Items.Add(new ListBoxItem { Content = name, Padding = new Thickness(12, 8) });
        }

        _nav.SelectionChanged += (_, _) => ShowPage(_nav.SelectedIndex);

        _defaultBar = InfoBar(
            Ui.Column(2,
                Ui.Text("Not your default browser", weight: FontWeight.SemiBold),
                Ui.Caption("Your desktop is still sending links somewhere else, so your rules will not run.")),
            Ui.Button("Fix this", MakeDefault, accent: true));

        _updateBar = InfoBar(_updateText, Ui.Button("Open the release page", () =>
        {
            if (_updateUrl is { } url)
            {
                AppState.OpenExternal(url);
            }
        }));
        _updateBar.IsVisible = false;

        var right = new DockPanel();
        DockPanel.SetDock(_defaultBar, Dock.Top);
        DockPanel.SetDock(_updateBar, Dock.Top);
        right.Children.Add(_defaultBar);
        right.Children.Add(_updateBar);
        right.Children.Add(_content);

        var root = new DockPanel();
        var navPanel = new DockPanel
        {
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Margin = new Thickness(16, 16, 16, 8),
                    [DockPanel.DockProperty] = Dock.Top,
                    Children =
                    {
                        new Image { Source = IconCache.Logo, Width = 24, Height = 24 },
                        Ui.Text(Core.AppInfo.Name, 14, FontWeight.SemiBold),
                    },
                },
                _nav,
            },
        };
        DockPanel.SetDock(navPanel, Dock.Left);
        root.Children.Add(navPanel);
        root.Children.Add(right);
        Content = root;

        _nav.SelectedIndex = 0;
        Activated += (_, _) => RefreshDefaultBar();
        Opened += async (_, _) => await CheckForUpdatesIfDueAsync();
        Closing += (_, _) => SaveWindowPlacement();
    }

    /// <summary>Re-renders the current page, e.g. after the config was changed elsewhere.</summary>
    public void ReloadPage() => ShowPage(_nav.SelectedIndex);

    private void ShowPage(int index)
    {
        if (index >= 0 && index < PageFactories.Length)
        {
            _content.Content = PageFactories[index].Create();
        }
    }

    private static Border InfoBar(Control body, Control action)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        grid.Children.Add(body);
        Grid.SetColumn(action, 1);
        grid.Children.Add(action);
        return new Border
        {
            Child = grid,
            Margin = new Thickness(24, 16, 24, 0),
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromArgb(0x30, 0xF0, 0xB0, 0x30)),
        };
    }

    private void MakeDefault()
    {
        try
        {
            PlatformServices.Current.Register();
            PlatformServices.Current.MakeDefaultBrowser();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceWarning($"Make default failed: {ex.Message}");
        }

        RefreshDefaultBar();
    }

    private void RefreshDefaultBar() =>
        _defaultBar.IsVisible = !PlatformServices.Current.IsDefaultBrowser(out _, out _);

    private async Task CheckForUpdatesIfDueAsync()
    {
        UpdateSettings settings = AppState.Config.Updates;
        if (!settings.AutoCheckEnabled)
        {
            return;
        }

        if (DateTimeOffset.TryParse(settings.LastCheckUtc, out DateTimeOffset last)
            && DateTimeOffset.UtcNow - last < TimeSpan.FromHours(Math.Clamp(settings.CheckIntervalHours, 1, 168)))
        {
            return;
        }

        UpdateCheckResult result = await UpdateNotifier.CheckAsync();
        ShowUpdate(result);
    }

    public void ShowUpdate(UpdateCheckResult result)
    {
        if (result.Status == UpdateCheckStatus.UpdateAvailable && result.ReleaseUrl is { Length: > 0 } url)
        {
            _updateUrl = url;
            _updateText.Text = $"Browser Wrangler {result.LatestVersion?.ToString(3)} is available.";
            _updateBar.IsVisible = true;
        }
    }

    private void SaveWindowPlacement()
    {
        if (WindowState != WindowState.Normal)
        {
            return;
        }

        AppState.MutateAndSave(c =>
        {
            c.Window.Width = (int)Width;
            c.Window.Height = (int)Height;
            c.Window.X = Position.X;
            c.Window.Y = Position.Y;
        });
    }
}
