using System.Diagnostics;
using BrowserWrangler.Core.Models;

namespace BrowserWrangler.Core.Launching;

/// <summary>
/// Launches a browser profile with a URL. Mirrors bt's browser_instance::launch.
/// </summary>
public static class BrowserLauncher
{
    /// <summary>
    /// Builds the full argument string for a launch: %url% substitution, app mode, user args.
    /// </summary>
    public static string BuildArguments(BrowserProfile profile, ClickPayload payload)
    {
        string encodedUrl = Uri.EscapeDataString(payload.Url);
        string arg = profile.LaunchArg.Length == 0
            ? payload.Url
            : ReplaceFirst(profile.LaunchArg, BrowserProfile.UrlArgName, payload.Url);
        arg = ReplaceFirst(arg, BrowserProfile.UrlEncodedArgName, encodedUrl);

        if (payload.AppMode && profile.Browser.SupportsFramelessWindows)
        {
            arg = $"--app={arg}";
        }

        if (profile.UserArg.Length > 0)
        {
            arg += " " + profile.UserArg;
        }

        return arg;
    }

    public static void Launch(BrowserProfile profile, ClickPayload payload)
    {
        string arg = BuildArguments(profile, payload);
        Browser browser = profile.Browser;

        if (browser.IsStoreApp)
        {
            // launch via shell activation of the app family
            string familyName = browser.OpenCommand[Browser.UwpCmdPrefix.Length..];
            Process.Start(new ProcessStartInfo($"shell:AppsFolder\\{familyName}!App")
            {
                UseShellExecute = true,
                Arguments = arg,
            });
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = browser.OpenCommand,
            Arguments = arg,
            UseShellExecute = false,
        };
        Process.Start(psi);
    }

    private static string ReplaceFirst(string input, string find, string replace)
    {
        int pos = input.IndexOf(find, StringComparison.Ordinal);
        return pos < 0 ? input : input[..pos] + replace + input[(pos + find.Length)..];
    }
}
