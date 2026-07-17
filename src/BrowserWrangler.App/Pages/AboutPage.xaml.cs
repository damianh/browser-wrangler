using Microsoft.UI.Xaml.Controls;

namespace BrowserWrangler.App.Pages;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
        string version = typeof(AboutPage).Assembly.GetName().Version?.ToString(3) ?? "dev";
        VersionText.Text = $"Version {version}";
    }
}
