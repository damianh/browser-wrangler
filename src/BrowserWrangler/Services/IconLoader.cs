using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.UI.Xaml.Media.Imaging;

namespace BrowserWrangler.Services;

/// <summary>Extracts and caches browser executable icons as XAML image sources.</summary>
public static class IconLoader
{
    private static readonly Dictionary<string, BitmapImage?> Cache = new(StringComparer.OrdinalIgnoreCase);

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
}
