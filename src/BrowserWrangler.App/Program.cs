using System.Runtime.InteropServices;
using BrowserWrangler.Core.Configuration;
using BrowserWrangler.Core.Launching;
using BrowserWrangler.Core.Models;
using BrowserWrangler.Core.Rules;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace BrowserWrangler.App;

internal enum LaunchMode
{
    Config,
    Picker,
    Toast,
}

/// <summary>State handed from Main to the XAML App instance.</summary>
internal static class LaunchContext
{
    public static LaunchMode Mode { get; set; } = LaunchMode.Config;
    public static AppConfig Config { get; set; } = null!;
    public static RouteDecision? Decision { get; set; }
    public static string ToastText { get; set; } = string.Empty;
}

public static class Program
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12; // Alt
    private const int VK_CAPITAL = 0x14;

    [STAThread]
    public static int Main(string[] args)
    {
        var store = new ConfigStore();
        AppConfig config = store.Load();
        LaunchContext.Config = config;

        string? url = args.FirstOrDefault(a =>
            a.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith(Core.AppInfo.CustomProtocol + ":", StringComparison.OrdinalIgnoreCase));

        if (url is not null)
        {
            return HandleUrl(config, url);
        }

        // config UI: single instance
        AppInstance main = AppInstance.FindOrRegisterForKey("browser-wrangler-config");
        if (!main.IsCurrent)
        {
            main.RedirectActivationToAsync(AppInstance.GetCurrent().GetActivatedEventArgs())
                .AsTask().GetAwaiter().GetResult();
            return 0;
        }

        LaunchContext.Mode = LaunchMode.Config;
        main.Activated += OnRedirectedActivation;
        StartXamlApp();
        return 0;
    }

    private static int HandleUrl(AppConfig config, string url)
    {
        if (url.StartsWith(Core.AppInfo.CustomProtocol + ":", StringComparison.OrdinalIgnoreCase))
        {
            // x-bw:<url> from browser extensions
            url = url[(Core.AppInfo.CustomProtocol.Length + 1)..].TrimStart('/');
        }

        var payload = new ClickPayload(url);
        bool pickerRequested = IsPickerHotkeyDown(config.Picker);

        RouteDecision decision = UrlRouter.Route(config, payload, pickerRequested);
        LaunchContext.Decision = decision;

        switch (decision.Action)
        {
            case RouteAction.Open:
                BrowserMatchResult match = decision.Matches[0];
                BrowserLauncher.Launch(match.Profile, decision.Payload);
                if (config.Toast.ShowOnOpen)
                {
                    LaunchContext.Mode = LaunchMode.Toast;
                    LaunchContext.ToastText =
                        $"{match.Profile.BestDisplayName}  \u2190  {(match.Rule.IsFallback ? "default" : match.Rule.Value)}";
                    StartXamlApp();
                }

                return 0;

            case RouteAction.ShowPicker:
                LaunchContext.Mode = LaunchMode.Picker;
                StartXamlApp();
                return 0;

            default:
                // no browsers configured - open config UI so the user can set up
                LaunchContext.Mode = LaunchMode.Config;
                StartXamlApp();
                return 0;
        }
    }

    private static bool IsPickerHotkeyDown(PickerSettings settings)
    {
        static bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

        bool ctrl = Down(VK_CONTROL);
        bool shift = Down(VK_SHIFT);
        bool alt = Down(VK_MENU);
        bool caps = (GetAsyncKeyState(VK_CAPITAL) & 0x0001) != 0;

        return (settings.OnCtrlShift && ctrl && shift)
            || (settings.OnCtrlAlt && ctrl && alt)
            || (settings.OnAltShift && alt && shift)
            || (settings.OnCapsLock && caps);
    }

    private static void OnRedirectedActivation(object? sender, AppActivationArguments e)
    {
        // bring existing config window to front
        App.Current?.DispatcherQueue?.TryEnqueue(() => App.CurrentApp?.ActivateMainWindow());
    }

    private static void StartXamlApp()
    {
        global::WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }
}
