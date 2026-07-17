using BrowserWrangler.App.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BrowserWrangler.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = "Browser Wrangler";
        ExtendsContentIntoTitleBar = true;
        Nav.SelectedItem = Nav.MenuItems[0];
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        string tag = (args.SelectedItem as NavigationViewItem)?.Tag as string ?? "browsers";
        System.Type page = tag switch
        {
            "rules" => typeof(RulesPage),
            "settings" => typeof(SettingsPage),
            "health" => typeof(HealthPage),
            "about" => typeof(AboutPage),
            _ => typeof(BrowsersPage),
        };
        ContentFrame.Navigate(page);
    }
}
