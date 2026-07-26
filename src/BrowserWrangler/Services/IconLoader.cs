using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using BrowserWrangler.Core.Models;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace BrowserWrangler.Services;

/// <summary>Extracts and caches browser/profile icons as XAML image sources.</summary>
public static class IconLoader
{
    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
    private const uint BI_RGB = 0;
    private const uint DIB_RGB_COLORS = 0;
    private const uint DI_NORMAL = 0x0003;

    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Profile-specific icon: user override or discovered profile picture when present,
    /// otherwise the browser executable's icon.
    /// </summary>
    public static ImageSource? GetIconForProfile(BrowserProfile profile)
    {
        string path = profile.GetBestIconPath();
        return IsImageFile(path) ? GetImageFile(path) : GetIconForExe(path);
    }

    public static ImageSource? GetIconForExe(string exePath)
    {
        if (Cache.TryGetValue(exePath, out ImageSource? cached))
        {
            return cached;
        }

        ImageSource? image = null;
        if (File.Exists(exePath))
        {
            nint result = SHGetFileInfo(
                exePath,
                FILE_ATTRIBUTE_NORMAL,
                out SHFILEINFO fileInfo,
                (uint)Marshal.SizeOf<SHFILEINFO>(),
                SHGFI_ICON | SHGFI_LARGEICON);
            if (result != nint.Zero && fileInfo.hIcon != nint.Zero)
            {
                try
                {
                    image = ConvertIconToImageSource(fileInfo.hIcon);
                }
                finally
                {
                    _ = DestroyIcon(fileInfo.hIcon);
                }
            } 
        }

        Cache[exePath] = image;
        return image;
    }

    private static ImageSource? GetImageFile(string path)
    {
        if (Cache.TryGetValue(path, out ImageSource? cached))
        {
            return cached;
        }

        BitmapImage? image = null;
        try
        {
            if (File.Exists(path))
            {
                // load via memory so the file isn't kept locked
                using var ms = new MemoryStream(File.ReadAllBytes(path));
                image = new BitmapImage();
                image.SetSource(ms.AsRandomAccessStream());
            }
        }
        catch (IOException)
        {
            // cosmetic; ignore failures
        }
        catch (UnauthorizedAccessException)
        {
            // cosmetic; ignore failures
        }

        Cache[path] = image;
        return image;
    }

    private static ImageSource? ConvertIconToImageSource(nint hIcon)
    {
        if (!GetIconInfo(hIcon, out ICONINFO iconInfo))
        {
            return null;
        }

        try
        {
            nint sizeBitmap = iconInfo.hbmColor != nint.Zero ? iconInfo.hbmColor : iconInfo.hbmMask;
            if (sizeBitmap == nint.Zero ||
                GetObject(sizeBitmap, Marshal.SizeOf<BITMAP>(), out BITMAP bitmap) == 0)
            {
                return null;
            }

            int width = bitmap.bmWidth;
            int height = Math.Abs(bitmap.bmHeight);
            if (iconInfo.hbmColor == nint.Zero)
            {
                height /= 2; // monochrome masks store AND/XOR planes stacked vertically
            }

            if (width <= 0 || height <= 0)
            {
                return null;
            }

            var bmi = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = width,
                    biHeight = -height, // top-down rows for XAML
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = BI_RGB,
                },
            };

            nint dc = CreateCompatibleDC(nint.Zero);
            if (dc == nint.Zero)
            {
                return null;
            }

            nint dibBits = nint.Zero;
            nint dib = nint.Zero;
            try
            {
                dib = CreateDIBSection(dc, ref bmi, DIB_RGB_COLORS, out dibBits, nint.Zero, 0);
                if (dib == nint.Zero || dibBits == nint.Zero)
                {
                    return null;
                }

                nint oldObject = SelectObject(dc, dib);
                if (oldObject == nint.Zero)
                {
                    return null;
                }

                try
                {
                    if (!DrawIconEx(dc, 0, 0, hIcon, width, height, 0, nint.Zero, DI_NORMAL))
                    {
                        return null;
                    }

                    byte[] pixels = new byte[width * height * 4];
                    Marshal.Copy(dibBits, pixels, 0, pixels.Length);

                    var wb = new WriteableBitmap(width, height);
                    using Stream pixelStream = wb.PixelBuffer.AsStream();
                    pixelStream.Write(pixels, 0, pixels.Length);
                    return wb;
                }
                finally
                {
                    _ = SelectObject(dc, oldObject);
                }
            }
            finally
            {
                if (dib != nint.Zero)
                {
                    _ = DeleteObject(dib);
                }

                _ = DeleteDC(dc);
            }
        }
        finally
        {
            if (iconInfo.hbmColor != nint.Zero)
            {
                _ = DeleteObject(iconInfo.hbmColor);
            }

            if (iconInfo.hbmMask != nint.Zero)
            {
                _ = DeleteObject(iconInfo.hbmMask);
            }
        }
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

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        out SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetIconInfo(nint hIcon, out ICONINFO piconinfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(nint hIcon);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteDC(nint hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint SelectObject(nint hdc, nint h);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreateDIBSection(
        nint hdc,
        ref BITMAPINFO pbmi,
        uint iUsage,
        out nint ppvBits,
        nint hSection,
        uint dwOffset);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DrawIconEx(
        nint hdc,
        int xLeft,
        int yTop,
        nint hIcon,
        int cxWidth,
        int cyWidth,
        uint istepIfAniCur,
        nint hbrFlickerFreeDraw,
        uint diFlags);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int GetObject(nint h, int c, out BITMAP pv);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(nint hObject);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public nint hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public uint xHotspot;
        public uint yHotspot;
        public nint hbmMask;
        public nint hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public nint bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
    }
}
