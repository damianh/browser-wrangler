using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace BrowserWrangler.App;

/// <summary>
/// Small transient notification shown bottom-right after a URL was routed.
/// </summary>
public sealed partial class ToastWindow : Window
{
    public ToastWindow(string text, int visibleSeconds)
    {
        InitializeComponent();
        ToastText.Text = text;

        var presenter = OverlappedPresenter.CreateForDialog();
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(false, false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;

        // bottom-right of the primary work area
        DisplayArea area = DisplayArea.Primary;
        const int width = 380;
        const int height = 52;
        AppWindow.MoveAndResize(new RectInt32(
            area.WorkArea.X + area.WorkArea.Width - width - 16,
            area.WorkArea.Y + area.WorkArea.Height - height - 16,
            width,
            height));

        DispatcherQueueTimer timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(Math.Max(1, visibleSeconds));
        timer.IsRepeating = false;
        timer.Tick += (_, _) => Close();
        timer.Start();
    }
}
