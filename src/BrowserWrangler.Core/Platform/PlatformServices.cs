using BrowserWrangler.Core.Platform.Linux;
using BrowserWrangler.Core.Platform.Windows;

namespace BrowserWrangler.Core.Platform;

public static class PlatformServices
{
    private static readonly Lazy<IPlatform> LazyCurrent = new(Create);

    public static IPlatform Current => LazyCurrent.Value;

    private static IPlatform Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsPlatform();
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxPlatform();
        }

        throw new PlatformNotSupportedException("Browser Wrangler supports Windows and Linux.");
    }
}
