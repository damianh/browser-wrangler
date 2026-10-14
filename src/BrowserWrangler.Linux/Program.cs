using Avalonia;
using BrowserWrangler.Core;
using BrowserWrangler.Core.Configuration;
using BrowserWrangler.Core.Launching;
using BrowserWrangler.Core.Logging;
using BrowserWrangler.Core.Models;
using BrowserWrangler.Core.Platform;
using BrowserWrangler.Core.Rules;
using BrowserWrangler.Linux.Services;

[assembly: System.Runtime.Versioning.SupportedOSPlatform("linux")]

namespace BrowserWrangler.Linux;

internal enum LaunchMode
{
    Config,
    Picker,
    Toast,
    Welcome,
}

/// <summary>State handed from Main to the Avalonia App instance.</summary>
internal static class LaunchContext
{
    public static LaunchMode Mode { get; set; } = LaunchMode.Config;
    public static RouteDecision? Decision { get; set; }
    public static string ToastText { get; set; } = string.Empty;
    public static SingleInstance? Instance { get; set; }
}

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--version"))
        {
            Console.WriteLine($"{AppInfo.Name} {DiagnosticsInfo.VersionDisplay}");
            return 0;
        }

        if (args.Contains("--help") || args.Contains("-h"))
        {
            Console.WriteLine($"""
                {AppInfo.Name} {DiagnosticsInfo.VersionDisplay}

                Usage: {AppInfo.LinuxAppId} [option | URL]

                  URL              Route the URL to a browser according to your rules
                  --register       Install the .desktop entry so the desktop lists us as a browser
                  --unregister     Remove the .desktop entry
                  --make-default   Register and set as the default browser for http/https
                  --first-run      Register and show the welcome window
                  --version        Print the version
                """);
            return 0;
        }

        IPlatform platform = PlatformServices.Current;
        AppConfig config = new ConfigStore().Load(DiagnosticsInfo.AppVersion);
        AppState.Config = config;

        if (args.Contains("--register"))
        {
            platform.Register();
            return 0;
        }

        if (args.Contains("--unregister"))
        {
            platform.Unregister();
            return 0;
        }

        if (args.Contains("--make-default"))
        {
            platform.Register();
            platform.MakeDefaultBrowser();
            return 0;
        }

        if (args.Contains("--first-run"))
        {
            platform.EnsureRegistered();
            return StartConfigInstance(args, LaunchMode.Welcome);
        }

        string? url = args.FirstOrDefault(a =>
            a.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith(AppInfo.CustomProtocol + ":", StringComparison.OrdinalIgnoreCase));

        if (url is not null)
        {
            return HandleUrl(args, config, url);
        }

        // a moved or upgraded install leaves a stale Exec= path behind - repair it silently
        platform.EnsureRegistered();

        bool needsSetup = !config.SetupCompleted && !platform.IsDefaultBrowser(out _, out _);
        return StartConfigInstance(args, needsSetup ? LaunchMode.Welcome : LaunchMode.Config);
    }

    private static int StartConfigInstance(string[] args, LaunchMode mode)
    {
        SingleInstance? instance = SingleInstance.TryAcquire();
        if (instance is null)
        {
            return 0;
        }

        using (instance)
        {
            LaunchContext.Instance = instance;
            return StartApp(args, mode);
        }
    }

    private static int HandleUrl(string[] args, AppConfig config, string url)
    {
        if (url.StartsWith(AppInfo.CustomProtocol + ":", StringComparison.OrdinalIgnoreCase))
        {
            // x-bw:<url> from browser extensions
            url = url[(AppInfo.CustomProtocol.Length + 1)..].TrimStart('/');
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
                    LaunchContext.ToastText =
                        $"{match.Profile.BestDisplayName}  \u2190  {(match.Rule.IsFallback ? "default" : match.Rule.Value)}";
                    return StartApp(args, LaunchMode.Toast);
                }

                return 0;

            case RouteAction.ShowPicker:
                return StartApp(args, LaunchMode.Picker);

            default:
                // no browsers configured - open config UI so the user can set up
                return StartConfigInstance(args, LaunchMode.Config);
        }
    }

    private static int StartApp(string[] args, LaunchMode mode)
    {
        LaunchContext.Mode = mode;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new X11PlatformOptions { WmClass = AppInfo.LinuxAppId })
            .LogToTrace();
}
