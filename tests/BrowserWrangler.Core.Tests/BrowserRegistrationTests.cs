using BrowserWrangler.Core;
using BrowserWrangler.Core.Setup;

namespace BrowserWrangler.Core.Tests;

public class BrowserRegistrationTests
{
    [Fact]
    public void Url_protocol_capabilities_include_http_https_and_custom_protocol()
    {
        IReadOnlyList<string> protocols = BrowserRegistration.GetRegisteredUrlProtocols();
        Assert.Contains("http", protocols);
        Assert.Contains("https", protocols);
        Assert.Contains(AppInfo.CustomProtocol, protocols);
    }

    [Fact]
    public void Html_file_capabilities_are_present_when_enabled()
    {
        IReadOnlyList<string> extensions = BrowserRegistration.GetRegisteredFileExtensions(includeHtmlFileAssociations: true);
        Assert.Equal(new[] { ".htm", ".html" }, extensions);
    }

    [Fact]
    public void Html_file_capabilities_are_empty_when_disabled()
    {
        Assert.Empty(BrowserRegistration.GetRegisteredFileExtensions(includeHtmlFileAssociations: false));
    }

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
