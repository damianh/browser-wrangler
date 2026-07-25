using BrowserWrangler.Core.Configuration;
using BrowserWrangler.Core.Setup;
using BrowserWrangler.Pages;
using BrowserWrangler.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace BrowserWrangler;

public sealed partial class MainWindow : Window
{
    private const int DefaultWidth = 1000;
    private const int DefaultHeight = 700;

    public MainWindow()
    {
        InitializeComponent();
        Title = "Browser Wrangler";
        ExtendsContentIntoTitleBar = true;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"));
        RestoreWindowBounds();
        Closed += (_, _) => SaveWindowBounds();
        Nav.SelectedItem = Nav.MenuItems[0];
        Activated += OnActivated;
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            return;
        }

        // re-check on every activation: the user may have just changed it in Windows Settings
        DefaultBrowserInfoBar.IsOpen = !BrowserRegistration.IsDefaultBrowser(out _, out _);
    }

    private void FixDefaultBrowser_Click(object sender, RoutedEventArgs e)
    {
        BrowserRegistration.EnsureRegistered();
        BrowserRegistration.OpenDefaultAppsSettings();
    }

    private void RestoreWindowBounds()
    {
        WindowSettings s = AppState.Config.Window;
        int width = s.Width > 200 ? s.Width : DefaultWidth;
        int height = s.Height > 200 ? s.Height : DefaultHeight;

        if (s.X != int.MinValue && s.Y != int.MinValue &&
            DisplayArea.GetFromRect(new RectInt32(s.X, s.Y, width, height), DisplayAreaFallback.None) is not null)
        {
            AppWindow.MoveAndResize(new RectInt32(s.X, s.Y, width, height));
        }
        else
        {
            AppWindow.Resize(new SizeInt32(width, height));
            CenterOnScreen(width, height);
        }
    }

    private void CenterOnScreen(int width, int height)
    {
        DisplayArea area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        AppWindow.Move(new PointInt32(
            area.WorkArea.X + (area.WorkArea.Width - width) / 2,
            area.WorkArea.Y + (area.WorkArea.Height - height) / 2));
    }

    private void SaveWindowBounds()
    {
        OverlappedPresenter? presenter = AppWindow.Presenter as OverlappedPresenter;
        if (presenter is { State: OverlappedPresenterState.Minimized or OverlappedPresenterState.Maximized })
        {
            return; // keep the last restored bounds
        }

        WindowSettings s = AppState.Config.Window;
        s.Width = AppWindow.Size.Width;
        s.Height = AppWindow.Size.Height;
        s.X = AppWindow.Position.X;
        s.Y = AppWindow.Position.Y;
        AppState.Save();
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        string tag = (args.SelectedItem as NavigationViewItem)?.Tag as string ?? "browsers";
        System.Type page = tag switch
        {
            "rules" => typeof(RulesPage),
            "rule-hits" => typeof(RuleHitsPage),
            "settings" => typeof(SettingsPage),
            "health" => typeof(HealthPage),
            "about" => typeof(AboutPage),
            _ => typeof(BrowsersPage),
        };
        ContentFrame.Navigate(page);
    }
}
