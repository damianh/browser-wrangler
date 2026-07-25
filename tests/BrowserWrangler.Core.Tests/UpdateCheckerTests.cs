using System.Net;
using System.Text;
using BrowserWrangler.Core.Updates;

namespace BrowserWrangler.Core.Tests;

public class UpdateCheckerTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(HttpResponseMessage response)
            : this(_ => response)
        {
        }

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_responder(request));
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string ReleaseJson(string tag) =>
        $$"""
        {
          "tag_name": "{{tag}}",
          "html_url": "https://github.com/damianh/browser-wrangler/releases/tag/{{tag}}",
          "prerelease": false
        }
        """;

    private static UpdateChecker MakeChecker(HttpMessageHandler handler) =>
        new(new HttpClient(handler), "https://api.example/releases/latest");

    [Fact]
    public async Task Reports_update_available_when_release_is_newer()
    {
        UpdateChecker checker = MakeChecker(new StubHandler(Json(
            """
            {
              "tag_name": "v2026.801.4",
              "html_url": "https://github.com/damianh/browser-wrangler/releases/tag/v2026.801.4",
              "prerelease": false,
              "assets": [
                {
                  "name": "BrowserWrangler-2026.801.4-x64-setup.exe",
                  "browser_download_url": "https://github.com/damianh/browser-wrangler/releases/download/v2026.801.4/BrowserWrangler-2026.801.4-x64-setup.exe"
                }
              ]
            }
            """)));

        UpdateCheckResult result = await checker.CheckAsync(new Version(2026, 718, 10), "x64");

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal(new Version(2026, 801, 4, 0), result.LatestVersion);
        Assert.Equal("https://github.com/damianh/browser-wrangler/releases/tag/v2026.801.4", result.ReleaseUrl);
        Assert.Equal(
            "https://github.com/damianh/browser-wrangler/releases/download/v2026.801.4/BrowserWrangler-2026.801.4-x64-setup.exe",
            result.InstallerDownloadUrl);
    }

    [Fact]
    public async Task Reports_up_to_date_when_release_matches_running_version()
    {
        UpdateChecker checker = MakeChecker(new StubHandler(Json(ReleaseJson("v2026.718.10"))));

        UpdateCheckResult result = await checker.CheckAsync(new Version(2026, 718, 10));

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
    }

    [Fact]
    public async Task Reports_up_to_date_when_running_version_is_ahead_of_release()
    {
        UpdateChecker checker = MakeChecker(new StubHandler(Json(ReleaseJson("v2026.718.10"))));

        UpdateCheckResult result = await checker.CheckAsync(new Version(2026, 719, 1));

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
    }

    [Fact]
    public async Task Reports_development_build_for_the_local_default_version()
    {
        UpdateChecker checker = MakeChecker(new StubHandler(Json(ReleaseJson("v2026.718.10"))));

        UpdateCheckResult result = await checker.CheckAsync(new Version(1, 0, 0));

        Assert.Equal(UpdateCheckStatus.DevelopmentBuild, result.Status);
        Assert.Equal(new Version(2026, 718, 10, 0), result.LatestVersion);
    }

    [Fact]
    public async Task Fails_when_tag_is_not_a_version()
    {
        UpdateChecker checker = MakeChecker(new StubHandler(Json(ReleaseJson("nightly"))));

        UpdateCheckResult result = await checker.CheckAsync(new Version(2026, 718, 10));

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Contains("nightly", result.Message);
    }

    [Fact]
    public async Task Fails_when_response_is_not_json()
    {
        UpdateChecker checker = MakeChecker(new StubHandler(Json("<html>nope</html>")));

        UpdateCheckResult result = await checker.CheckAsync(new Version(2026, 718, 10));

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Equal("GitHub returned a response that could not be understood.", result.Message);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"just a string\"")]
    [InlineData("123")]
    [InlineData("null")]
    [InlineData("{ \"tag_name\": 123 }")]
    public async Task Fails_when_json_response_has_unexpected_shape(string body)
    {
        UpdateChecker checker = MakeChecker(new StubHandler(Json(body)));

        UpdateCheckResult result = await checker.CheckAsync(new Version(2026, 718, 10));

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Equal("GitHub returned a response that could not be understood.", result.Message);
    }

    [Fact]
    public async Task Reports_update_available_when_release_url_is_not_a_string()
    {
        UpdateChecker checker = MakeChecker(new StubHandler(Json("{ \"tag_name\": \"v2026.801.4\", \"html_url\": 42, \"prerelease\": false }")));

        UpdateCheckResult result = await checker.CheckAsync(new Version(2026, 718, 10));

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal(new Version(2026, 801, 4, 0), result.LatestVersion);
        Assert.Null(result.ReleaseUrl);
    }

    [Fact]
    public async Task Fails_when_latest_release_is_prerelease()
    {
        UpdateChecker checker = MakeChecker(new StubHandler(Json(
            """
            {
              "tag_name": "v2026.801.4",
              "html_url": "https://github.com/damianh/browser-wrangler/releases/tag/v2026.801.4",
              "prerelease": true
            }
            """)));

        UpdateCheckResult result = await checker.CheckAsync(new Version(2026, 718, 10));

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Contains("stable", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("https://github.com/damianh/browser-wrangler/releases/download/v2026.801.4/BrowserWrangler-2026.801.4-x64-setup.exe")]
    [InlineData("https://objects.githubusercontent.com/github-production-release-asset-2e65be/123/abc")]
    [InlineData("https://release-assets.githubusercontent.com/github-production-release-asset-2e65be/123/abc")]
    public void Accepts_trusted_asset_urls(string url)
    {
        Assert.True(Uri.TryCreate(url, UriKind.Absolute, out Uri? uri));
        Assert.NotNull(uri);
        Assert.True(UpdateChecker.IsTrustedReleaseAssetUri(uri));
    }

    [Theory]
    [InlineData("http://github.com/damianh/browser-wrangler/releases/download/v2026.801.4/BrowserWrangler-2026.801.4-x64-setup.exe")]
    [InlineData("https://example.com/file.exe")]
    [InlineData("https://github.com/other-owner/other-repo/releases/download/v1/file.exe")]
    public void Rejects_untrusted_asset_urls(string url)
    {
        Assert.True(Uri.TryCreate(url, UriKind.Absolute, out Uri? uri));
        Assert.NotNull(uri);
        Assert.False(UpdateChecker.IsTrustedReleaseAssetUri(uri));
    }

    [Fact]
    public async Task Fails_with_rate_limit_message_when_github_throttles()
    {
        var throttled = new HttpResponseMessage(HttpStatusCode.Forbidden);
        throttled.Headers.Add("x-ratelimit-remaining", "0");
        UpdateChecker checker = MakeChecker(new StubHandler(throttled));

        UpdateCheckResult result = await checker.CheckAsync(new Version(2026, 718, 10));

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Contains("rate limit", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Fails_when_no_releases_are_published()
    {
        UpdateChecker checker = MakeChecker(new StubHandler(new HttpResponseMessage(HttpStatusCode.NotFound)));

        UpdateCheckResult result = await checker.CheckAsync(new Version(2026, 718, 10));

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Contains("releases", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Fails_gracefully_when_the_network_is_unavailable()
    {
        UpdateChecker checker = MakeChecker(new StubHandler(_ => throw new HttpRequestException("no such host")));

        UpdateCheckResult result = await checker.CheckAsync(new Version(2026, 718, 10));

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Contains("no such host", result.Message);
    }

    [Fact]
    public async Task Fails_gracefully_when_the_request_times_out()
    {
        UpdateChecker checker = MakeChecker(new StubHandler(_ => throw new TaskCanceledException("timed out")));

        UpdateCheckResult result = await checker.CheckAsync(new Version(2026, 718, 10));

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Contains("timed out", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("v2026.718.10", 2026, 718, 10)]
    [InlineData("2026.718.10", 2026, 718, 10)]
    [InlineData("V2026.718", 2026, 718, 0)]
    [InlineData("  v2026.718.10  ", 2026, 718, 10)]
    public void Parses_supported_release_tag_shapes(string tag, int major, int minor, int build)
    {
        Assert.True(UpdateChecker.TryParseVersionTag(tag, out Version version));
        Assert.Equal(new Version(major, minor, build, 0), version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("release-2026")]
    public void Rejects_unparseable_release_tags(string? tag) =>
        Assert.False(UpdateChecker.TryParseVersionTag(tag, out _));

    [Fact]
    public void Configured_client_sends_a_user_agent_because_github_requires_one()
    {
        using HttpClient client = UpdateChecker.CreateHttpClient(new Version(2026, 718, 10));

        Assert.Contains("BrowserWrangler", client.DefaultRequestHeaders.UserAgent.ToString());
        Assert.Contains(client.DefaultRequestHeaders.Accept, header => header.MediaType == "application/vnd.github+json");
        Assert.Equal(TimeSpan.FromSeconds(10), client.Timeout);
    }
}
