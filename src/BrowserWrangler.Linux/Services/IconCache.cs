using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace BrowserWrangler.Linux.Services;

/// <summary>Loads browser icons (PNG/JPEG files) once and caches them; SVG and missing icons return null.</summary>
internal static class IconCache
{
    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.Ordinal);
    private static Bitmap? _logo;

    public static Bitmap Logo => _logo ??= new Bitmap(AssetLoader.Open(new Uri("avares://browser-wrangler/Assets/logo-256.png")));

    public static Bitmap? Get(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        if (Cache.TryGetValue(path, out Bitmap? cached))
        {
            return cached;
        }

        Bitmap? bitmap = null;
        if (File.Exists(path) && !path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith(".svgz", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using FileStream stream = File.OpenRead(path);
                bitmap = Bitmap.DecodeToWidth(stream, 64);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                or NotSupportedException or InvalidOperationException)
            {
                bitmap = null;
            }
        }

        Cache[path] = bitmap;
        return bitmap;
    }
}
