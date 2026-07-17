using System.Runtime.InteropServices;
using BrowserWrangler.Core.Configuration;
using BrowserWrangler.Core.Launching;
using BrowserWrangler.Core.Models;
using BrowserWrangler.Core.Rules;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace BrowserWrangler;

/// <summary>
/// Compact browser picker shown at the mouse cursor. Number keys 1-9 pick, Esc cancels.
/// </summary>
public sealed partial class PickerWindow : Window
{
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point pt);

    private struct Point
    {
        public int X;
        public int Y;
    }

    private readonly AppConfig _config;
    private readonly RouteDecision _decision;
    private readonly List<BrowserProfile> _profiles;

    public PickerWindow(AppConfig config, RouteDecision decision)
    {
        _config = config;
        _decision = decision;
        InitializeComponent();

        // when multiple rules matched, offer just those; otherwise offer everything visible
        _profiles = decision.Matches.Count > 1 && !decision.Matches[0].Rule.IsFallback
            ? decision.Matches.Select(m => m.Profile).ToList()
            : RuleMatcher.ToProfiles(config.Browsers);

        UrlText.Text = decision.Payload.Url;
        BuildButtons();
        ConfigureWindow();
    }

    private void BuildButtons()
    {
        int index = 1;
        foreach (BrowserProfile profile in _profiles)
        {
            var label = new StackPanel { Spacing = 2 };
            label.Children.Add(new TextBlock { Text = profile.BestDisplayName });
            if (_config.Picker.ShowKeyHints && index <= 9)
            {
                label.Children.Add(new TextBlock
                {
                    Text = index.ToString(),
                    FontSize = 10,
                    Opacity = 0.6,
                    HorizontalAlignment = HorizontalAlignment.Center,
                });
            }

            var button = new Button { Content = label, Tag = profile, Padding = new Thickness(12, 8, 12, 8) };
            button.Click += (_, _) => Pick((BrowserProfile)button.Tag);
            ProfileList.Items.Add(button);
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

        // size to content, position at cursor
        int width = Math.Max(240, 140 * Math.Min(_profiles.Count, 6));
        int height = 110;
        GetCursorPos(out Point pt);
        appWindow.MoveAndResize(new Windows.Graphics.RectInt32(pt.X - width / 2, pt.Y - height / 2, width, height));

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

    private void Pick(BrowserProfile profile)
    {
        BrowserLauncher.Launch(profile, _decision.Payload);
        Close();
    }
}
