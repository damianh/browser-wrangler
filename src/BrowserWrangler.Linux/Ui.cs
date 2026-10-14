using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BrowserWrangler.Linux.Services;

namespace BrowserWrangler.Linux;

/// <summary>Small factory helpers so the UI can be built in code (AOT-friendly, no bindings).</summary>
internal static class Ui
{
    public static TextBlock Text(string text, double size = 14, FontWeight weight = FontWeight.Normal, double opacity = 1) =>
        new()
        {
            Text = text,
            FontSize = size,
            FontWeight = weight,
            Opacity = opacity,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };

    public static TextBlock Heading(string text) => Text(text, 20, FontWeight.SemiBold);

    public static TextBlock Subheading(string text) => Text(text, 15, FontWeight.SemiBold);

    public static TextBlock Caption(string text) => Text(text, 12, opacity: 0.7);

    public static Button Button(string content, Action onClick, bool accent = false)
    {
        var button = new Button { Content = content, VerticalAlignment = VerticalAlignment.Center };
        if (accent)
        {
            button.Classes.Add("accent");
        }

        button.Click += (_, _) => onClick();
        return button;
    }

    public static StackPanel Row(double spacing, params Control[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing };
        panel.Children.AddRange(children);
        return panel;
    }

    public static StackPanel Column(double spacing, params Control[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, Spacing = spacing };
        panel.Children.AddRange(children);
        return panel;
    }

    public static Border Card(Control child) =>
        new()
        {
            Child = child,
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0x80, 0x80, 0x80)),
            Background = new SolidColorBrush(Color.FromArgb(0x10, 0x80, 0x80, 0x80)),
        };

    public static ToggleSwitch Toggle(bool value, Action<bool> changed, string? on = null, string? off = null)
    {
        var toggle = new ToggleSwitch { IsChecked = value, OnContent = on ?? string.Empty, OffContent = off ?? string.Empty };
        toggle.IsCheckedChanged += (_, _) => changed(toggle.IsChecked == true);
        return toggle;
    }

    public static CheckBox Check(string label, bool value, Action<bool> changed)
    {
        var box = new CheckBox { Content = label, IsChecked = value };
        box.IsCheckedChanged += (_, _) => changed(box.IsChecked == true);
        return box;
    }

    /// <summary>Browser icon, or a letter glyph when the icon is missing or SVG-only.</summary>
    public static Control Icon(string path, string name, double size)
    {
        if (IconCache.Get(path) is { } bitmap)
        {
            return new Image { Source = bitmap, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        }

        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 4),
            Background = new SolidColorBrush(Color.FromArgb(0x40, 0x80, 0x80, 0x80)),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = name.Length > 0 ? name[..1].ToUpperInvariant() : "?",
                FontSize = size * 0.5,
                FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    public static async Task CopyToClipboardAsync(Visual visual, string text)
    {
        if (TopLevel.GetTopLevel(visual)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    public static ScrollViewer Scroll(Control content) =>
        new() { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
}
