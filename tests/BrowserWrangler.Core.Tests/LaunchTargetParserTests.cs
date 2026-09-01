using BrowserWrangler.Core.Launching;

namespace BrowserWrangler.Core.Tests;

public class LaunchTargetParserTests
{
    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://example.com/path?q=1")]
    [InlineData("x-bw:https://example.com")]
    public void Url_and_custom_protocol_targets_are_always_accepted(string candidate)
    {
        bool ok = LaunchTargetParser.TryNormalizeLaunchTarget(candidate, "x-bw", allowHtmlFileTargets: false, out string parsed);

        Assert.True(ok);
        Assert.Equal(candidate, parsed);
    }

    [Fact]
    public void Html_file_targets_are_rejected_when_disabled()
    {
        Assert.False(LaunchTargetParser.TryNormalizeLaunchTarget("file:///C:/temp/page.html", "x-bw", allowHtmlFileTargets: false, out _));
        Assert.False(LaunchTargetParser.TryNormalizeLaunchTarget(@"C:\temp\page.html", "x-bw", allowHtmlFileTargets: false, out _));
    }

    [Fact]
    public void Html_file_uri_is_accepted_when_enabled()
    {
        bool ok = LaunchTargetParser.TryNormalizeLaunchTarget("file:///C:/temp/page.HTM", "x-bw", allowHtmlFileTargets: true, out string parsed);

        Assert.True(ok);
        Assert.StartsWith("file:///", parsed, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("/page.HTM", parsed, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Local_html_path_is_normalized_to_file_uri_when_enabled()
    {
        string localPath = Path.Combine(Path.GetTempPath(), "Browser Wrangler", "Page.html");

        bool ok = LaunchTargetParser.TryNormalizeLaunchTarget(localPath, "x-bw", allowHtmlFileTargets: true, out string parsed);

        Assert.True(ok);
        Assert.StartsWith("file:///", parsed, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Page.html", parsed, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Non_html_file_targets_are_rejected()
    {
        Assert.False(LaunchTargetParser.TryNormalizeLaunchTarget("file:///C:/temp/page.txt", "x-bw", allowHtmlFileTargets: true, out _));
        Assert.False(LaunchTargetParser.TryNormalizeLaunchTarget(@"C:\temp\page.txt", "x-bw", allowHtmlFileTargets: true, out _));
    }

    [Fact]
    public void Parser_returns_first_supported_target_in_argument_list()
    {
        string[] args = ["--first-run", @"C:\temp\doc.html", "https://example.com"];

        bool ok = LaunchTargetParser.TryGetLaunchTargetUrl(args, "x-bw", allowHtmlFileTargets: true, out string parsed);

        Assert.True(ok);
        Assert.StartsWith("file:///", parsed, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("/doc.html", parsed, StringComparison.OrdinalIgnoreCase);
    }
}
