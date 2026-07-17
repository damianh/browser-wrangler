using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace BrowserWrangler.App;

public partial class App : Application
{
    private Window? _window;

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

        switch (LaunchContext.Mode)
        {
            case LaunchMode.Picker:
                _window = new PickerWindow(LaunchContext.Config, LaunchContext.Decision!);
                break;
            case LaunchMode.Toast:
                _window = new ToastWindow(LaunchContext.ToastText, LaunchContext.Config.Toast.VisibleSeconds);
                break;
            default:
                _window = new MainWindow();
                break;
        }

        _window.Activate();
    }

    public void ActivateMainWindow() => _window?.Activate();
}
