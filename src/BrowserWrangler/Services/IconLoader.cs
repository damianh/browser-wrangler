using System.Drawing;
using System.Drawing.Imaging;
using BrowserWrangler.Core.Models;
using Microsoft.UI.Xaml.Media.Imaging;

namespace BrowserWrangler.Services;

/// <summary>Extracts and caches browser/profile icons as XAML image sources.</summary>
public static class IconLoader
{
    private static readonly Dictionary<string, BitmapImage?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Profile-specific icon: user override or discovered profile picture when present,
    /// otherwise the browser executable's icon.
    /// </summary>
    public static BitmapImage? GetIconForProfile(BrowserProfile profile)
    {
        string path = profile.GetBestIconPath();
        return IsImageFile(path) ? GetImageFile(path) : GetIconForExe(path);
    }

    public static BitmapImage? GetIconForExe(string exePath)
    {
        if (Cache.TryGetValue(exePath, out BitmapImage? cached))
        {
            return cached;
        }

        BitmapImage? image = null;
        try
        {
            if (File.Exists(exePath) && Icon.ExtractAssociatedIcon(exePath) is { } icon)
            {
                using (icon)
                using (Bitmap bitmap = icon.ToBitmap())
                {
                    var ms = new MemoryStream();
                    bitmap.Save(ms, ImageFormat.Png);
                    ms.Position = 0;
                    image = new BitmapImage();
                    image.SetSource(ms.AsRandomAccessStream());
                }
            }
        }
        catch
        {
            // icon extraction is cosmetic; ignore failures
        }

        Cache[exePath] = image;
        return image;
    }

    private static BitmapImage? GetImageFile(string path)
    {
        if (Cache.TryGetValue(path, out BitmapImage? cached))
        {
            return cached;
        }

        BitmapImage? image = null;
        try
        {
            if (File.Exists(path))
            {
                // load via memory so the file isn't kept locked
                var ms = new MemoryStream(File.ReadAllBytes(path));
                image = new BitmapImage();
                image.SetSource(ms.AsRandomAccessStream());
            }
        }
        catch
        {
            // cosmetic; ignore failures
        }

        Cache[path] = image;
        return image;
    }

    private static bool IsImageFile(string path)
    {
        string ext = Path.GetExtension(path);
        return ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".gif", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".ico", StringComparison.OrdinalIgnoreCase);
    }
}
