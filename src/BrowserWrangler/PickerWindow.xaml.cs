using System.Runtime.InteropServices;
using BrowserWrangler.Core.Configuration;
using BrowserWrangler.Core.Launching;
using BrowserWrangler.Core.Models;
using BrowserWrangler.Core.Rules;
using BrowserWrangler.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace BrowserWrangler;

/// <summary>
/// Compact vertical browser picker shown at the mouse cursor, styled after bt:
/// numbered shortcuts, browser icons, URL header with copy. Esc cancels.
/// </summary>
public sealed partial class PickerWindow : Window
{
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint pt);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;
    private const int DWMWA_BORDER_COLOR = 34;
    private const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;

    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private const int RowHeight = 44;
    private const int HeaderHeight = 78;
    private const int WindowWidth = 480;
    private const int MaxVisibleRows = 10;

    private readonly AppConfig _config;
    private readonly RouteDecision _decision;
    private readonly List<BrowserProfile> _profiles;

    public PickerWindow(AppConfig config, RouteDecision decision)
    {
        _config = config;
        _decision = decision;
        InitializeComponent();

        // when multiple real rules matched, offer just those; otherwise everything visible
        _profiles = decision.Matches.Count > 1 && !decision.Matches[0].Rule.IsFallback
            ? decision.Matches.Select(m => m.Profile).Where(p => !p.IsHidden && !p.Browser.IsHidden).ToList()
            : RuleMatcher.ToProfiles(config.Browsers);

        UrlText.Text = decision.Payload.Url;
        ToolTipService.SetToolTip(UrlText, decision.Payload.Url);
        BuildRows();
        ConfigureWindow();
    }

    private void BuildRows()
    {
        int index = 1;
        foreach (BrowserProfile profile in _profiles)
        {
            var row = new Grid { ColumnSpacing = 10, Padding = new Thickness(8, 0, 8, 0), Height = 38 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            if (_config.Picker.ShowKeyHints && index <= 9)
            {
                var hint = new TextBlock
                {
                    Text = index.ToString(),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
                };
                row.Children.Add(hint);
            }

            BitmapImage? icon = IconLoader.GetIconForProfile(profile);
            if (icon is not null)
            {
                var img = new Image { Source = icon, Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(img, 1);
                row.Children.Add(img);
            }
            else
            {
                var fallbackIcon = new FontIcon { Glyph = "\uE774", FontSize = 18, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(fallbackIcon, 1);
                row.Children.Add(fallbackIcon);
            }

            var name = new TextBlock
            {
                Text = profile.BestDisplayName,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(name, 2);
            row.Children.Add(name);

            var button = new Button
            {
                Content = row,
                Tag = profile,
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            };
            button.Click += (_, _) => Pick((BrowserProfile)button.Tag);
            ProfileList.Children.Add(button);
            index++;
        }
    }

    private void ConfigureWindow()
    {
        AppWindow appWindow = AppWindow;
        var presenter = OverlappedPresenter.CreateForDialog();
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(false, false);
        appWindow.SetPresenter(presenter);
        appWindow.IsShownInSwitchers = false;

        // round the actual window corners so they match the XAML border,
        // and remove the native DWM border so only the XAML 1px stroke shows
        nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        int corner = DWMWCP_ROUND;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        int none = unchecked((int)DWMWA_COLOR_NONE);
        _ = DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref none, sizeof(int));

        int rows = Math.Min(Math.Max(_profiles.Count, 1), MaxVisibleRows);
        double scale = GetDpiForWindow(hwnd) / 96.0;
        int width = (int)(WindowWidth * scale);
        int height = (int)((HeaderHeight + (rows * RowHeight) + 14) * scale);

        // center on the display the cursor is on
        GetCursorPos(out NativePoint pt);
        DisplayArea area = DisplayArea.GetFromPoint(new Windows.Graphics.PointInt32(pt.X, pt.Y), DisplayAreaFallback.Nearest);
        int x = area.WorkArea.X + ((area.WorkArea.Width - width) / 2);
        int y = area.WorkArea.Y + ((area.WorkArea.Height - height) / 2);
        appWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, width, height));

        if (Content is UIElement root)
        {
            root.KeyDown += OnKeyDown;
        }

        if (_config.Picker.CloseOnFocusLoss)
        {
            Activated += (_, e) =>
            {
                if (e.WindowActivationState == WindowActivationState.Deactivated)
                {
                    Close();
                }
            };
        }
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            Close();
            return;
        }

        int number = e.Key switch
        {
            >= VirtualKey.Number1 and <= VirtualKey.Number9 => e.Key - VirtualKey.Number1,
            >= VirtualKey.NumberPad1 and <= VirtualKey.NumberPad9 => e.Key - VirtualKey.NumberPad1,
            _ => -1,
        };
        if (number >= 0 && number < _profiles.Count)
        {
            Pick(_profiles[number]);
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(_decision.Payload.Url);
        Clipboard.SetContent(package);
        Clipboard.Flush();
        Close();
    }

    private void Pick(BrowserProfile profile)
    {
        BrowserLauncher.Launch(profile, _decision.Payload);
        Close();
    }
}
