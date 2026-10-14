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
            : ReplaceFirst(
                ReplaceFirst(profile.LaunchArg, BrowserProfile.UrlEncodedArgName, encodedUrl),
                BrowserProfile.UrlArgName,
                payload.Url);

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

        Process.Start(CreateStartInfo(browser.OpenCommand, arg));
    }

    /// <summary>
    /// On Windows the open command is a bare executable path. On Linux it is the tokenised Exec
    /// line of a .desktop file (e.g. "flatpak run org.mozilla.firefox"), so split off the program.
    /// </summary>
    public static ProcessStartInfo CreateStartInfo(string openCommand, string arg)
    {
        string fileName = openCommand;
        string arguments = arg;
        if (!OperatingSystem.IsWindows())
        {
            List<string> tokens = CommandLine.Split(openCommand);
            if (tokens.Count > 0)
            {
                fileName = tokens[0];
                string prefix = CommandLine.Join(tokens.Skip(1));
                arguments = prefix.Length == 0 ? arg : $"{prefix} {arg}";
            }
        }

        return new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
        };
    }

    private static string ReplaceFirst(string input, string find, string replace)
    {
        int pos = input.IndexOf(find, StringComparison.Ordinal);
        return pos < 0 ? input : input[..pos] + replace + input[(pos + find.Length)..];
    }
}
