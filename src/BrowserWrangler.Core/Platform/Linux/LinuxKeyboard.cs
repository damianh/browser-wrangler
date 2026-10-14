using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace BrowserWrangler.Core.Platform.Linux;

/// <summary>
/// Keyboard state at click time. Caps Lock comes from the LED class in sysfs (world-readable,
/// works on Wayland and X11). Modifier keys are only observable on X11 via XQueryKeymap -
/// Wayland deliberately hides global keyboard state from clients.
/// </summary>
[SupportedOSPlatform("linux")]
public static unsafe class LinuxKeyboard
{
    private const string LedsDirectory = "/sys/class/leds";

    // X11 keysyms
    private const ulong XK_Shift_L = 0xffe1;
    private const ulong XK_Shift_R = 0xffe2;
    private const ulong XK_Control_L = 0xffe3;
    private const ulong XK_Control_R = 0xffe4;
    private const ulong XK_Alt_L = 0xffe9;
    private const ulong XK_Alt_R = 0xffea;

    public static bool IsX11Session
    {
        get
        {
            string sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? string.Empty;
            if (sessionType.Equals("wayland", StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
            {
                return false;
            }

            return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"));
        }
    }

    public static bool HasCapsLockLed => CapsLockLedDirectories().Any();

    public static bool IsCapsLockOn()
    {
        foreach (string dir in CapsLockLedDirectories())
        {
            try
            {
                string value = File.ReadAllText(Path.Combine(dir, "brightness")).Trim();
                if (value.Length > 0 && value != "0")
                {
                    return true;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return false;
    }

    /// <summary>Queries held modifiers from the X server; all false when unavailable.</summary>
    public static (bool Ctrl, bool Shift, bool Alt) QueryX11Modifiers()
    {
        if (!IsX11Session || !NativeLibrary.TryLoad("libX11.so.6", out nint lib))
        {
            return default;
        }

        try
        {
            var openDisplay = (delegate* unmanaged<byte*, nint>)NativeLibrary.GetExport(lib, "XOpenDisplay");
            var closeDisplay = (delegate* unmanaged<nint, int>)NativeLibrary.GetExport(lib, "XCloseDisplay");
            var queryKeymap = (delegate* unmanaged<nint, byte*, int>)NativeLibrary.GetExport(lib, "XQueryKeymap");
            var keysymToKeycode = (delegate* unmanaged<nint, nuint, byte>)NativeLibrary.GetExport(lib, "XKeysymToKeycode");

            nint display = openDisplay(null);
            if (display == 0)
            {
                return default;
            }

            try
            {
                byte* keys = stackalloc byte[32];
                queryKeymap(display, keys);

                bool Down(ulong keysym)
                {
                    byte code = keysymToKeycode(display, (nuint)keysym);
                    return code != 0 && (keys[code / 8] & (1 << (code % 8))) != 0;
                }

                return (
                    Down(XK_Control_L) || Down(XK_Control_R),
                    Down(XK_Shift_L) || Down(XK_Shift_R),
                    Down(XK_Alt_L) || Down(XK_Alt_R));
            }
            finally
            {
                closeDisplay(display);
            }
        }
        catch (EntryPointNotFoundException)
        {
            return default;
        }
        finally
        {
            NativeLibrary.Free(lib);
        }
    }

    private static IEnumerable<string> CapsLockLedDirectories()
    {
        try
        {
            return Directory.Exists(LedsDirectory)
                ? Directory.EnumerateDirectories(LedsDirectory, "*capslock*").ToList()
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
