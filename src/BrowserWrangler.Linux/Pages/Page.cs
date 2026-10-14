using Avalonia;
using Avalonia.Controls;

namespace BrowserWrangler.Linux.Pages;

/// <summary>Base for config pages: a heading plus scrollable content.</summary>
internal abstract class Page : UserControl
{
    protected Page(string title)
    {
        Body = new StackPanel { Spacing = 12, Margin = new Thickness(0, 0, 12, 24) };
        Header = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        Header.Children.Add(Ui.Heading(title));
        var root = new DockPanel();
        DockPanel.SetDock(Header, Dock.Top);
        root.Children.Add(Header);
        root.Children.Add(Ui.Scroll(Body));
        Content = root;
    }

    protected DockPanel Header { get; }

    protected StackPanel Body { get; }

    protected void AddHeaderAction(Control action)
    {
        action.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        Header.Children.Insert(0, action);
        DockPanel.SetDock(action, Dock.Right);
    }
}
