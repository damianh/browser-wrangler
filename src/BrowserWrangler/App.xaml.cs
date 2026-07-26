using System.Diagnostics;
using System.Security;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using BrowserWrangler.Services;
using Microsoft.Data.Sqlite;

namespace BrowserWrangler;

public partial class App : Application
{
    private Window? _window;
    private MainWindow? _mainWindow;

    public static new App? Current => Application.Current as App;

    public static App? CurrentApp { get; private set; }

    public DispatcherQueue? DispatcherQueue { get; private set; }

    public App()
    {
        CurrentApp = this;
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        DispatcherQueue = DispatcherQueue.GetForCurrentThread();

        if (LaunchContext.Mode is LaunchMode.Config or LaunchMode.Welcome)
        {
            try
            {
                AppState.RefreshBrowsers();
            }
            catch (IOException ex)
            {
                Trace.TraceWarning($"Startup browser refresh failed (I/O): {ex}");
            }
            catch (UnauthorizedAccessException ex)
            {
                Trace.TraceWarning($"Startup browser refresh failed (access denied): {ex}");
            }
            catch (SecurityException ex)
            {
                Trace.TraceWarning($"Startup browser refresh failed (security): {ex}");
            }
            catch (SqliteException ex)
            {
                Trace.TraceWarning($"Startup browser refresh failed (SQLite): {ex}");
            }
        }

        switch (LaunchContext.Mode)
        {
            case LaunchMode.Picker:
                _window = new PickerWindow(LaunchContext.Config, LaunchContext.Decision!);
                break;
            case LaunchMode.Toast:
                _window = new ToastWindow(LaunchContext.ToastText, LaunchContext.Config.Toast.VisibleSeconds);
                break;
            case LaunchMode.Welcome:
                _window = new WelcomeWindow();
                break;
            default:
                _window = _mainWindow = new MainWindow();
                break;
        }

        _window.Activate();
    }

    /// <summary>Opens (or brings forward) the config window, e.g. from the first-run window.</summary>
    public void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            _mainWindow = new MainWindow();
            _mainWindow.Closed += (_, _) => _mainWindow = null;
        }

        _window = _mainWindow;
        _mainWindow.Activate();
    }

    public void ActivateMainWindow() => (_mainWindow as Window ?? _window)?.Activate();
}
