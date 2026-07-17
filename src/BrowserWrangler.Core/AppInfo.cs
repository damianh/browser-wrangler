namespace BrowserWrangler.Core;

/// <summary>App-wide constants (equivalent of bt's globals.h).</summary>
public static class AppInfo
{
    public const string Name = "Browser Wrangler";

    /// <summary>ProgId registered for http/https URL associations.</summary>
    public const string ProgId = "BrowserWranglerHTM";

    /// <summary>Custom protocol for browser extensions ("x-bw:...").</summary>
    public const string CustomProtocol = "x-bw";

    public const string Description =
        "Redirects links to the right browser or browser profile based on your rules.";
}
