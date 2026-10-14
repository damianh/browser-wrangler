using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BrowserWrangler.Core.Configuration;
using BrowserWrangler.Core.Launching;
using BrowserWrangler.Core.Logging;
using BrowserWrangler.Core.Models;
using BrowserWrangler.Core.Rules;

namespace BrowserWrangler.Linux.Windows;

/// <summary>
/// Compact vertical browser picker: numbered shortcuts, browser icons, URL header with copy.
/// Esc cancels.
/// </summary>
internal sealed class PickerWindow : Window
{
    private const int WindowWidth = 480;
    private const int RowHeight = 40;
    private const int MaxVisibleRows = 10;

    private readonly AppConfig _config;
    private readonly RouteDecision _decision;
    private readonly List<BrowserProfile> _profiles;
    private readonly ListBox _list;
    private bool _isPicking;

    public PickerWindow(AppConfig config, RouteDecision decision)
    {
        _config = config;
        _decision = decision;

        // when multiple real rules matched, offer just those; otherwise everything visible
        _profiles = decision.Matches.Count > 1 && !decision.Matches[0].Rule.IsFallback
            ? decision.Matches.Select(m => m.Profile).Where(p => !p.IsHidden && !p.Browser.IsHidden).ToList()
            : RuleMatcher.ToProfiles(config.Browsers);

        Title = Core.AppInfo.Name;
        Icon = new WindowIcon(Services.IconCache.Logo);
        Width = WindowWidth;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        WindowDecorations = WindowDecorations.BorderOnly;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var urlText = new TextBlock
        {
            Text = decision.Payload.Url,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.8,
        };
        ToolTip.SetTip(urlText, decision.Payload.Url);

        var copy = new Button { Content = "Copy", Padding = new Thickness(8, 2) };
        ToolTip.SetTip(copy, "Copy URL and close");
        copy.Click += async (_, _) =>
        {
            await Ui.CopyToClipboardAsync(this, _decision.Payload.Url);
            Close();
        };
        var close = new Button { Content = "\u2715", Padding = new Thickness(8, 2) };
        ToolTip.SetTip(close, "Close (Esc)");
        close.Click += (_, _) => Close();

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 8 };
        var logo = new Image { Source = Services.IconCache.Logo, Width = 20, Height = 20 };
        header.Children.Add(logo);
        Grid.SetColumn(urlText, 1);
        header.Children.Add(urlText);
        Grid.SetColumn(copy, 2);
        header.Children.Add(copy);
        Grid.SetColumn(close, 3);
        header.Children.Add(close);

        _list = new ListBox
        {
            MaxHeight = MaxVisibleRows * RowHeight,
            Background = Brushes.Transparent,
        };
        BuildRows();

        Content = new DockPanel
        {
            Margin = new Thickness(10),
            Children =
            {
                new Border { Child = header, Margin = new Thickness(0, 0, 0, 8), [DockPanel.DockProperty] = Dock.Top },
                _profiles.Count == 0
                    ? Ui.Caption("No browsers found. Open Browser Wrangler to discover browsers.")
                    : _list,
            },
        };

        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Opened += (_, _) =>
        {
            Activate();
            Dispatcher.UIThread.Post(FocusSelected, DispatcherPriority.Input);
        };

        if (_config.Picker.CloseOnFocusLoss)
        {
            Deactivated += (_, _) =>
            {
                if (!_isPicking)
                {
                    Close();
                }
            };
        }
    }

    private void BuildRows()
    {
        int index = 1;
        foreach (BrowserProfile profile in _profiles)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("18,28,*"), ColumnSpacing = 8, Height = RowHeight - 8 };
            string? hint = index <= 9 ? index.ToString() : index == 10 ? "0" : null;
            if (_config.Picker.ShowKeyHints && hint is not null)
            {
                row.Children.Add(new TextBlock { Text = hint, FontSize = 12, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center });
            }

            Control icon = Ui.Icon(profile.GetBestIconPath(), profile.Browser.Name, 24);
            Grid.SetColumn(icon, 1);
            row.Children.Add(icon);

            var name = new TextBlock
            {
                Text = profile.BestDisplayName,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(name, 2);
            row.Children.Add(name);

            var item = new ListBoxItem { Content = row, Tag = profile, Padding = new Thickness(6, 4) };
            item.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton == MouseButton.Left)
                {
                    Pick(profile);
                }
            };
            _list.Items.Add(item);
            index++;
        }

        if (_list.ItemCount > 0)
        {
            _list.SelectedIndex = 0;
        }
    }

    private void FocusSelected()
    {
        if (_list.SelectedIndex >= 0 && _list.ContainerFromIndex(_list.SelectedIndex) is { } container)
        {
            container.Focus(NavigationMethod.Directional);
        }
        else
        {
            _list.Focus();
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                Close();
                return;
            case Key.Enter or Key.Space:
                if (_list.SelectedItem is ListBoxItem { Tag: BrowserProfile selected })
                {
                    e.Handled = true;
                    Pick(selected);
                }

                return;
        }

        int number = e.Key switch
        {
            Key.D0 or Key.NumPad0 => 9,
            >= Key.D1 and <= Key.D9 => e.Key - Key.D1,
            >= Key.NumPad1 and <= Key.NumPad9 => e.Key - Key.NumPad1,
            _ => -1,
        };

        if (number >= 0 && number < _profiles.Count)
        {
            e.Handled = true;
            Pick(_profiles[number]);
        }
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
