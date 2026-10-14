using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace BrowserWrangler.Linux.Windows;

/// <summary>Small transient notification shown bottom-right after a URL was routed.</summary>
internal sealed class ToastWindow : Window
{
    private const int ToastWidth = 380;
    private const int ToastHeight = 52;

    public ToastWindow(string text, int visibleSeconds)
    {
        Title = Core.AppInfo.Name;
        Width = ToastWidth;
        Height = ToastHeight;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        WindowDecorations = WindowDecorations.None;

        Content = new Border
        {
            Padding = new Thickness(12, 0),
            Child = Ui.Row(
                10,
                new Image { Source = Services.IconCache.Logo, Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = ToastWidth - 60 }),
        };

        Opened += (_, _) =>
        {
            if ((Screens.ScreenFromWindow(this) ?? Screens.Primary) is { } screen)
            {
                PixelRect area = screen.WorkingArea;
                double scale = screen.Scaling;
                Position = new PixelPoint(
                    area.Right - (int)((ToastWidth + 16) * scale),
                    area.Bottom - (int)((ToastHeight + 16) * scale));
            }

            DispatcherTimer.RunOnce(Close, TimeSpan.FromSeconds(Math.Max(1, visibleSeconds)));
        };
    }
}
