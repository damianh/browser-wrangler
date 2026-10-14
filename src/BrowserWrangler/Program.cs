using BrowserWrangler.Core.Configuration;
using BrowserWrangler.Core.Launching;
using BrowserWrangler.Core.Logging;
using BrowserWrangler.Core.Models;
using BrowserWrangler.Core.Platform;
using BrowserWrangler.Core.Rules;
using BrowserWrangler.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace BrowserWrangler;

internal enum LaunchMode
{
    Config,
    Picker,
    Toast,
    Welcome,
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
    [STAThread]
    public static int Main(string[] args)
    {
        var store = new ConfigStore();
        AppConfig config = store.Load(DiagnosticsInfo.AppVersion);
        LaunchContext.Config = config;

        // installer hooks: silent register/unregister of the browser (per-user registry)
        if (args.Contains("--register", StringComparer.OrdinalIgnoreCase))
        {
            Core.Setup.BrowserRegistration.RegisterAll();
            return 0;
        }

        if (args.Contains("--unregister", StringComparer.OrdinalIgnoreCase))
        {
            Core.Setup.BrowserRegistration.UnregisterAll();
            return 0;
        }

        // installer hook: post-install setup window
        bool firstRun = args.Contains("--first-run", StringComparer.OrdinalIgnoreCase);
        if (firstRun)
        {
            Core.Setup.BrowserRegistration.EnsureRegistered();
            return StartConfigInstance(LaunchMode.Welcome);
        }

        string? url = args.FirstOrDefault(a =>
            a.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith(Core.AppInfo.CustomProtocol + ":", StringComparison.OrdinalIgnoreCase));

        if (url is not null)
        {
            return HandleUrl(config, url);
        }

        // a moved or upgraded install leaves stale registry paths behind - repair them silently
        Core.Setup.BrowserRegistration.EnsureRegistered();

        // users who never saw the post-install window get it the first time they open the app
        bool needsSetup = !config.SetupCompleted &&
            !Core.Setup.BrowserRegistration.IsDefaultBrowser(out _, out _);

        return StartConfigInstance(needsSetup ? LaunchMode.Welcome : LaunchMode.Config);
    }

    /// <summary>Starts the single-instance UI process, redirecting to an existing instance if any.</summary>
    private static int StartConfigInstance(LaunchMode mode)
    {
        AppInstance main = AppInstance.FindOrRegisterForKey(Core.AppInfo.ConfigInstanceKey);
        if (!main.IsCurrent)
        {
            main.RedirectActivationToAsync(AppInstance.GetCurrent().GetActivatedEventArgs())
                .AsTask().GetAwaiter().GetResult();
            return 0;
        }

        LaunchContext.Mode = mode;
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
        bool pickerRequested = PlatformServices.Current.IsPickerHotkeyDown(config.Picker);

        RouteDecision decision = UrlRouter.Route(config, payload, pickerRequested);
        LaunchContext.Decision = decision;

        switch (decision.Action)
        {
            case RouteAction.Open:
                BrowserMatchResult match = decision.Matches[0];
                RuleHitLogger.LogDirectOpen(config, decision.Payload, match);
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
