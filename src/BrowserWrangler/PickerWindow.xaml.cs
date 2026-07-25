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

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, nint processId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint attachTo, uint attachFrom, bool attach);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hwnd, int nCmdShow);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;
    private const int DWMWA_BORDER_COLOR = 34;
    private const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;
    private const int SW_SHOWNORMAL = 1;

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
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _foregroundRetryTimer;
    private int _foregroundRetriesLeft;
    private bool _isPicking;

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
            var row = new Grid { ColumnSpacing = 10, Padding = new Thickness(8, 0, 8, 0), Height = 38, Tag = profile };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            string? hintText = index <= 9 ? index.ToString() : index == 10 ? "0" : null;
            if (_config.Picker.ShowKeyHints && hintText is not null)
            {
                var hint = new TextBlock
                {
                    Text = hintText,
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

            var item = new ListViewItem
            {
                Content = row,
                Tag = profile,
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            item.Tapped += (_, _) => Pick(profile);
            ProfileList.Items.Add(item);
            index++;
        }

        if (ProfileList.Items.Count > 0)
        {
            ProfileList.SelectedIndex = 0;
            ProfileList.Loaded += (_, _) => FocusSelected();
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
        Title = "Browser Wrangler";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarHost);

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

        if (Content is FrameworkElement root)
        {
            // handledEventsToo so the ListView's own key handling doesn't swallow shortcuts
            root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnKeyDown), true);
            root.Loaded += (_, _) => StartForegroundRetries();
        }

        Activated += OnFirstActivated;

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

    private void OnFirstActivated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState == WindowActivationState.Deactivated)
        {
            return;
        }

        Activated -= OnFirstActivated;
        StartForegroundRetries();
    }

    private void FocusSelected()
    {
        if (ProfileList.Items.Count == 0)
        {
            return;
        }

        if (ProfileList.SelectedIndex < 0)
        {
            ProfileList.SelectedIndex = 0;
        }

        if (ProfileList.ContainerFromIndex(ProfileList.SelectedIndex) is ListViewItem container)
        {
            _ = container.Focus(FocusState.Programmatic);
            return;
        }

        _ = ProfileList.Focus(FocusState.Programmatic);
    }

    private void MoveSelection(VirtualKey key)
    {
        int count = ProfileList.Items.Count;
        if (count == 0)
        {
            return;
        }

        int current = ProfileList.SelectedIndex;
        int next = key switch
        {
            VirtualKey.Home => 0,
            VirtualKey.End => count - 1,
            VirtualKey.Down => current < 0 ? 0 : (current + 1) % count,
            _ => current <= 0 ? count - 1 : current - 1,
        };

        ProfileList.SelectedIndex = next;
        ProfileList.ScrollIntoView(ProfileList.Items[next]);
        if (ProfileList.ContainerFromIndex(next) is ListViewItem container)
        {
            _ = container.Focus(FocusState.Programmatic);
        }
    }

    private void StartForegroundRetries()
    {
        if (TryFocusPicker())
        {
            return;
        }

        _foregroundRetriesLeft = 8;
        if (_foregroundRetryTimer is null)
        {
            _foregroundRetryTimer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
            _foregroundRetryTimer.Interval = TimeSpan.FromMilliseconds(60);
            _foregroundRetryTimer.Tick += (_, _) =>
            {
                if (TryFocusPicker() || --_foregroundRetriesLeft <= 0)
                {
                    _foregroundRetryTimer?.Stop();
                }
            };
        }

        _foregroundRetryTimer.Stop();
        _foregroundRetryTimer.Start();
    }

    private bool TryFocusPicker()
    {
        ForceForeground();
        FocusSelected();
        return GetForegroundWindow() == WinRT.Interop.WindowNative.GetWindowHandle(this);
    }

    /// <summary>
    /// The picker is launched from whatever app owns the link, so Windows' foreground lock can
    /// leave it visible but without keyboard focus. Borrow the foreground thread's input state
    /// long enough to take focus for real.
    /// </summary>
    private void ForceForeground()
    {
        nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        nint foreground = GetForegroundWindow();
        if (foreground == hwnd)
        {
            return;
        }

        uint foregroundThread = GetWindowThreadProcessId(foreground, 0);
        uint currentThread = GetCurrentThreadId();
        bool attached = foregroundThread != 0 && foregroundThread != currentThread
            && AttachThreadInput(currentThread, foregroundThread, true);

        _ = ShowWindow(hwnd, SW_SHOWNORMAL);
        _ = BringWindowToTop(hwnd);
        _ = SetForegroundWindow(hwnd);

        if (attached)
        {
            _ = AttachThreadInput(currentThread, foregroundThread, false);
        }
    }

    private void ProfileList_ItemClick(object sender, ItemClickEventArgs e)
    {
        BrowserProfile? profile = e.ClickedItem switch
        {
            ListViewItem { Tag: BrowserProfile p } => p,
            FrameworkElement { Tag: BrowserProfile p } => p,
            BrowserProfile p => p,
            _ => null,
        };

        if (profile is not null)
        {
            Pick(profile);
        }
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            Close();
            return;
        }

        if (e.Key is VirtualKey.Enter or VirtualKey.Space)
        {
            if (ProfileList.SelectedItem is ListViewItem { Tag: BrowserProfile selected })
            {
                e.Handled = true;
                Pick(selected);
            }

            return;
        }

        if (e.Key is VirtualKey.Down or VirtualKey.Up or VirtualKey.Home or VirtualKey.End)
        {
            // only step manually when the list didn't move selection itself (e.g. focus is elsewhere)
            if (!e.Handled)
            {
                e.Handled = true;
                MoveSelection(e.Key);
            }

            return;
        }

        int number = e.Key switch
        {
            VirtualKey.Number0 or VirtualKey.NumberPad0 => 9,
            >= VirtualKey.Number1 and <= VirtualKey.Number9 => e.Key - VirtualKey.Number1,
            >= VirtualKey.NumberPad1 and <= VirtualKey.NumberPad9 => e.Key - VirtualKey.NumberPad1,
            _ => -1,
        };
        if (number >= 0 && number < _profiles.Count)
        {
            e.Handled = true;
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

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Pick(BrowserProfile profile)
    {
        if (_isPicking)
        {
            return;
        }

        _isPicking = true;
        RuleHitLogger.LogPickerSelection(_config, _decision, profile);
        BrowserLauncher.Launch(profile, _decision.Payload);
        Close();
    }
}
