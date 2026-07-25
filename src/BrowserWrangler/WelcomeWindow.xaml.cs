using BrowserWrangler.Core.Setup;
using BrowserWrangler.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;

namespace BrowserWrangler;

/// <summary>
/// Compact first-run window shown after install. Confirms the browser registration and walks the
/// user through making Browser Wrangler the default browser, which only Windows Settings can do.
/// </summary>
public sealed partial class WelcomeWindow : Window
{
    private const int WindowWidth = 620;
    private const int WindowHeight = 480;

    private readonly DispatcherQueueTimer _pollTimer;

    public WelcomeWindow()
    {
        InitializeComponent();
        Title = "Welcome to Browser Wrangler";
        ExtendsContentIntoTitleBar = false;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"));
        LoadAppIcon();

        var presenter = OverlappedPresenter.CreateForDialog();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        AppWindow.SetPresenter(presenter);
        CenterOnScreen();

        Refresh();

        // Windows gives no notification when the default browser changes, so poll while we are open.
        _pollTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _pollTimer.Interval = TimeSpan.FromSeconds(1);
        _pollTimer.IsRepeating = true;
        _pollTimer.Tick += (_, _) => Refresh();
        _pollTimer.Start();

        Closed += (_, _) =>
        {
            _pollTimer.Stop();
            MarkSetupCompleted();
        };
    }

    private void CenterOnScreen()
    {
        DisplayArea area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        AppWindow.MoveAndResize(new RectInt32(
            area.WorkArea.X + ((area.WorkArea.Width - WindowWidth) / 2),
            area.WorkArea.Y + ((area.WorkArea.Height - WindowHeight) / 2),
            WindowWidth,
            WindowHeight));
    }

    private void LoadAppIcon()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "logo-256.png");
        if (!File.Exists(path))
        {
            return;
        }

        AppIcon.Source = new BitmapImage(new Uri(path));
        AppIcon.Visibility = Visibility.Visible;
    }

    private void Refresh()
    {
        bool registered = BrowserRegistration.IsRegisteredAsBrowser(out _);
        SetStatus(RegisterIcon, registered);
        RegisterButton.Visibility = registered ? Visibility.Collapsed : Visibility.Visible;
        RegisterDetail.Text = registered
            ? "Browser Wrangler now shows up in the Windows list of browsers."
            : "Browser Wrangler is not in the Windows list of browsers yet.";

        bool isDefault = BrowserRegistration.IsDefaultBrowser(out bool http, out bool https);
        SetStatus(DefaultIcon, isDefault);
        DefaultButton.Content = isDefault ? "Open Windows Settings" : "Set as default";
        DefaultDetail.Text = isDefault
            ? "Windows sends HTTP and HTTPS links to Browser Wrangler. You are all set."
            : http || https
                ? $"Still missing: {(http ? "HTTPS" : "HTTP")}. Open Windows Settings and pick Browser Wrangler for it."
                : "Windows only lets you do this yourself. Click the button, then pick Browser Wrangler for HTTP and HTTPS.";
    }

    private static void SetStatus(Microsoft.UI.Xaml.Controls.FontIcon icon, bool ok)
    {
        icon.Glyph = ok ? "\uE73E" : "\uEA39";
        icon.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            ok ? Microsoft.UI.Colors.Green : Microsoft.UI.Colors.OrangeRed);
    }

    private static void MarkSetupCompleted()
    {
        if (AppState.Config.SetupCompleted)
        {
            return;
        }

        AppState.Config.SetupCompleted = true;
        AppState.Save();
    }

    private void Register_Click(object sender, RoutedEventArgs e)
    {
        BrowserRegistration.RegisterAll();
        Refresh();
    }

    private void OpenDefaultApps_Click(object sender, RoutedEventArgs e) =>
        BrowserRegistration.OpenDefaultAppsSettings();

    private void OpenApp_Click(object sender, RoutedEventArgs e)
    {
        MarkSetupCompleted();
        App.CurrentApp?.ShowMainWindow();
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
