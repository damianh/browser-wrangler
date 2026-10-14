using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using BrowserWrangler.Linux.Services;
using BrowserWrangler.Linux.Windows;

namespace BrowserWrangler.Linux;

internal sealed class App : Application
{
    private MainWindow? _mainWindow;

    public static App? CurrentApp => Current as App;

    public override void Initialize()
    {
        Name = Core.AppInfo.Name;
        Styles.Add(new FluentTheme());
        ApplyTheme(AppState.Config.Theme);
    }

    public static void ApplyTheme(string theme)
    {
        if (Current is { } app)
        {
            app.RequestedThemeVariant = theme switch
            {
                "light" => ThemeVariant.Light,
                "dark" => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;

            if (LaunchContext.Mode is LaunchMode.Config or LaunchMode.Welcome)
            {
                AppState.TryRefreshBrowsers();
            }

            Window window = LaunchContext.Mode switch
            {
                LaunchMode.Picker => new PickerWindow(AppState.Config, LaunchContext.Decision!),
                LaunchMode.Toast => new ToastWindow(LaunchContext.ToastText, AppState.Config.Toast.VisibleSeconds),
                LaunchMode.Welcome => new WelcomeWindow(),
                _ => _mainWindow = CreateMainWindow(),
            };

            desktop.MainWindow = window;
            LaunchContext.Instance?.StartListening(() => Dispatcher.UIThread.Post(ActivateMainWindow));
        }

        base.OnFrameworkInitializationCompleted();
    }

    private MainWindow CreateMainWindow()
    {
        var window = new MainWindow();
        window.Closed += (_, _) => _mainWindow = null;
        return window;
    }

    /// <summary>Opens (or brings forward) the config window, e.g. from the first-run window.</summary>
    public void ShowMainWindow()
    {
        _mainWindow ??= CreateMainWindow();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = _mainWindow;
        }

        _mainWindow.Show();
        _mainWindow.Activate();
    }

    public void ActivateMainWindow()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
        {
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }

            window.Activate();
        }
        else
        {
            ShowMainWindow();
        }
    }
}
