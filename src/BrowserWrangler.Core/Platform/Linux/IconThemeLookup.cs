namespace BrowserWrangler.Core.Platform.Linux;

/// <summary>
/// Minimal freedesktop icon-theme lookup: resolves an icon name to the largest raster image in
/// the hicolor theme (which every app installs into), falling back to scalable SVG and pixmaps.
/// </summary>
public static class IconThemeLookup
{
    private static readonly int[] Sizes = [256, 192, 128, 96, 64, 48, 32, 24, 16];

    public static string Resolve(string icon, XdgDirectories dirs)
    {
        if (icon.Length == 0)
        {
            return string.Empty;
        }

        if (Path.IsPathRooted(icon))
        {
            return File.Exists(icon) ? icon : string.Empty;
        }

        List<string> iconRoots =
        [
            Path.Combine(dirs.Home, ".icons"),
            .. new[] { dirs.DataHome }.Concat(dirs.DataDirs).Select(d => Path.Combine(d, "icons")),
        ];

        foreach (int size in Sizes)
        {
            foreach (string root in iconRoots)
            {
                string candidate = Path.Combine(root, "hicolor", $"{size}x{size}", "apps", icon + ".png");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        foreach (string root in iconRoots)
        {
            string candidate = Path.Combine(root, "hicolor", "scalable", "apps", icon + ".svg");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        foreach (string dataDir in dirs.DataDirs)
        {
            string candidate = Path.Combine(dataDir, "pixmaps", icon + ".png");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return string.Empty;
    }
}
