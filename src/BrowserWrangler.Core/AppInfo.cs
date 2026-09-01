namespace BrowserWrangler.Core;

/// <summary>App-wide constants (equivalent of bt's globals.h).</summary>
public static class AppInfo
{
    public const string StableName = "Browser Wrangler";
    public const string DevName = "Browser Wrangler (Dev Channel)";

#if DEV_CHANNEL
    public const bool IsDevChannel = true;
    public const string Name = DevName;

    /// <summary>ProgId registered for URL associations (and optional .htm/.html file associations).</summary>
    public const string ProgId = "BrowserWranglerHTM.Dev";

    /// <summary>Custom protocol for browser extensions ("x-bw:...").</summary>
    public const string CustomProtocol = "x-bw-dev";
    public const string LocalDataDirectoryName = "BrowserWrangler-Dev";
    public const string ConfigInstanceKey = "browser-wrangler-dev-config";
    public const string ReleaseChannel = "dev";
#else
    public const bool IsDevChannel = false;
    public const string Name = StableName;

    /// <summary>ProgId registered for URL associations (and optional .htm/.html file associations).</summary>
    public const string ProgId = "BrowserWranglerHTM";

    /// <summary>Custom protocol for browser extensions ("x-bw:...").</summary>
    public const string CustomProtocol = "x-bw";
    public const string LocalDataDirectoryName = "BrowserWrangler";
    public const string ConfigInstanceKey = "browser-wrangler-config";
    public const string ReleaseChannel = "stable";
#endif

    public const string Description =
        "Redirects links to the right browser or browser profile based on your rules.";
}
