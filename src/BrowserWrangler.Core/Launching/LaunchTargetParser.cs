namespace BrowserWrangler.Core.Launching;

/// <summary>Parses and normalizes incoming launch arguments into routable targets.</summary>
public static class LaunchTargetParser
{
    public static bool TryGetLaunchTargetUrl(
        IEnumerable<string> args,
        string customProtocol,
        bool allowHtmlFileTargets,
        out string launchTargetUrl)
    {
        foreach (string arg in args)
        {
            if (TryNormalizeLaunchTarget(arg, customProtocol, allowHtmlFileTargets, out launchTargetUrl))
            {
                return true;
            }
        }

        launchTargetUrl = string.Empty;
        return false;
    }

    public static bool TryNormalizeLaunchTarget(
        string candidate,
        string customProtocol,
        bool allowHtmlFileTargets,
        out string launchTargetUrl)
    {
        launchTargetUrl = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        if (candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(customProtocol + ":", StringComparison.OrdinalIgnoreCase))
        {
            launchTargetUrl = candidate;
            return true;
        }

        if (!allowHtmlFileTargets)
        {
            return false;
        }

        if (Uri.TryCreate(candidate, UriKind.Absolute, out Uri? parsedUri)
            && parsedUri.Scheme == Uri.UriSchemeFile
            && IsHtmlPath(parsedUri.LocalPath))
        {
            launchTargetUrl = parsedUri.AbsoluteUri;
            return true;
        }

        if (!Path.IsPathRooted(candidate) || !IsHtmlPath(candidate))
        {
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(candidate);
            launchTargetUrl = new Uri(fullPath).AbsoluteUri;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or UriFormatException)
        {
            return false;
        }
    }

    private static bool IsHtmlPath(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".htm", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".html", StringComparison.OrdinalIgnoreCase);
    }
}
