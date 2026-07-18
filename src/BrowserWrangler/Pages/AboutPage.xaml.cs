using Microsoft.UI.Xaml.Controls;

namespace BrowserWrangler.Pages;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
        string version = typeof(AboutPage).Assembly.GetName().Version?.ToString(3) ?? "dev";
        VersionText.Text = $"Version {version}";
    }
}
