using System.Runtime.InteropServices;
using BrowserWrangler.Core.Setup;
using BrowserWrangler.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;

namespace BrowserWrangler;

/// <summary>
/// Compact first-run window shown after install. Confirms the browser registration and walks the
/// user through making Browser Wrangler the default browser, which only Windows Settings can do.
/// </summary>
public sealed partial class WelcomeWindow : Window
{
    private const int WindowWidth = 640;
    private const int WindowHeight = 560;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private readonly DispatcherQueueTimer _pollTimer;

    public WelcomeWindow()
    {
        InitializeComponent();
        Title = "Welcome to Browser Wrangler";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"));
        LoadAppIcon();

        // draw our own title bar so it follows the app theme instead of staying light
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarArea);

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
            MarkSetupCompletedIfDefault();
        };
    }

    private void CenterOnScreen()
    {
        // AppWindow works in physical pixels, so scale the layout size by the window's DPI
        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        uint dpi = GetDpiForWindow(hwnd);
        double scale = dpi == 0 ? 1.0 : dpi / 96.0;
        DisplayArea area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        // Clamp to the work area so high DPI on a small display cannot place the dialog off-screen.
        int width = Math.Min((int)Math.Round(WindowWidth * scale), area.WorkArea.Width);
        int height = Math.Min((int)Math.Round(WindowHeight * scale), area.WorkArea.Height);
        int x = Math.Max(area.WorkArea.X, area.WorkArea.X + ((area.WorkArea.Width - width) / 2));
        int y = Math.Max(area.WorkArea.Y, area.WorkArea.Y + ((area.WorkArea.Height - height) / 2));

        AppWindow.MoveAndResize(new RectInt32(
            x,
            y,
            width,
            height));
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

    /// <summary>
    /// Records completion only once Windows actually routes links to us, so dismissing the window
    /// early brings it back on the next launch rather than silently giving up.
    /// </summary>
    private static void MarkSetupCompletedIfDefault()
    {
        if (BrowserRegistration.IsDefaultBrowser(out _, out _))
        {
            MarkSetupCompleted();
        }
    }

    /// <summary>Records that the user has dealt with setup, default browser or not.</summary>
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
        BrowserRegistration.RegisterAll(AppState.Config.HandleHtmlFiles);
        Refresh();
    }

    private void OpenDefaultApps_Click(object sender, RoutedEventArgs e) =>
        BrowserRegistration.OpenDefaultAppsSettings();

    private void OpenApp_Click(object sender, RoutedEventArgs e)
    {
        // going into the app is the deliberate "not now" exit, so it stops the window reappearing
        MarkSetupCompleted();
        App.CurrentApp?.ShowMainWindow();
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void CloseAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Close();
    }
}
