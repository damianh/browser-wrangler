using BrowserWrangler.Core.Setup;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BrowserWrangler.App.Pages;

public sealed partial class HealthPage : Page
{
    private readonly List<SystemCheck> _checks = BrowserRegistration.GetChecks();

    public HealthPage()
    {
        InitializeComponent();
        Recheck();
    }

    private void Recheck()
    {
        CheckList.Children.Clear();
        foreach (SystemCheck check in _checks)
        {
            check.Recheck();

            var panel = new StackPanel { Spacing = 4 };
            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            header.Children.Add(new FontIcon
            {
                Glyph = check.IsOk ? "\uE73E" : "\uEA39",
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    check.IsOk ? Microsoft.UI.Colors.Green : Microsoft.UI.Colors.OrangeRed),
                VerticalAlignment = VerticalAlignment.Center,
            });
            header.Children.Add(new TextBlock
            {
                Text = check.Name,
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                VerticalAlignment = VerticalAlignment.Center,
            });
            if (!check.IsOk)
            {
                var fixButton = new Button { Content = "Fix" };
                fixButton.Click += (_, _) =>
                {
                    check.Fix();
                    Recheck();
                };
                header.Children.Add(fixButton);
            }

            panel.Children.Add(header);
            panel.Children.Add(new TextBlock { Text = check.Description, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
            if (!check.IsOk && check.ErrorMessage.Length > 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = check.ErrorMessage,
                    FontSize = 11,
                    Opacity = 0.6,
                    TextWrapping = TextWrapping.Wrap,
                });
            }

            CheckList.Children.Add(new Border
            {
                Child = panel,
                Padding = new Thickness(12),
                CornerRadius = new CornerRadius(6),
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            });
        }
    }

    private void Register_Click(object sender, RoutedEventArgs e)
    {
        BrowserRegistration.RegisterAll();
        Recheck();
    }

    private void Unregister_Click(object sender, RoutedEventArgs e)
    {
        BrowserRegistration.UnregisterAll();
        Recheck();
    }

    private void DefaultApps_Click(object sender, RoutedEventArgs e) =>
        BrowserRegistration.OpenDefaultAppsSettings();

    private void Recheck_Click(object sender, RoutedEventArgs e) => Recheck();
}
