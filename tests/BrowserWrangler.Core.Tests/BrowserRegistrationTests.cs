using BrowserWrangler.Core.Setup;

namespace BrowserWrangler.Core.Tests;

public class BrowserRegistrationTests
{
    [Fact]
    public void Default_apps_uri_deep_links_to_our_entry_on_windows_11()
    {
        string uri = BrowserRegistration.BuildDefaultAppsUri(22631);
        Assert.Equal("ms-settings:defaultapps?registeredAppUser=Browser%20Wrangler", uri);
    }

    [Theory]
    [InlineData(19045)]
    [InlineData(17763)]
    public void Default_apps_uri_falls_back_to_the_page_on_windows_10(int build)
    {
        Assert.Equal("ms-settings:defaultapps", BrowserRegistration.BuildDefaultAppsUri(build));
    }

    [Fact]
    public void Default_apps_uri_deep_links_from_the_first_windows_11_build()
    {
        Assert.StartsWith(
            "ms-settings:defaultapps?registeredAppUser=",
            BrowserRegistration.BuildDefaultAppsUri(22000),
            StringComparison.Ordinal);
    }
}
