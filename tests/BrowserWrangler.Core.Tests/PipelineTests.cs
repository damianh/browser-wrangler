using BrowserWrangler.Core.Models;
using BrowserWrangler.Core.Pipeline;
using System.Net;

namespace BrowserWrangler.Core.Tests;

public class PipelineTests
{
    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, CancellationToken, HttpResponseMessage>> _responses;

        public StubHttpMessageHandler(params HttpResponseMessage[] responses)
            : this(responses.Select(response => new Func<HttpRequestMessage, CancellationToken, HttpResponseMessage>((_, _) => response)).ToArray())
        {
        }

        public StubHttpMessageHandler(params Func<HttpRequestMessage, CancellationToken, HttpResponseMessage>[] responses)
        {
            _responses = new Queue<Func<HttpRequestMessage, CancellationToken, HttpResponseMessage>>(responses);
        }

        private HttpResponseMessage DequeueResponse(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No stubbed response available.");
            }

            return _responses.Dequeue()(request, cancellationToken);
        }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) => DequeueResponse(request, cancellationToken);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(DequeueResponse(request, cancellationToken));
    }

    private static Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> DelayedResponse(
        TimeSpan delay,
        HttpResponseMessage response) =>
        (_, cancellationToken) =>
        {
            if (cancellationToken.WaitHandle.WaitOne(delay))
            {
                throw new OperationCanceledException(cancellationToken);
            }

            return response;
        };

    [Fact]
    public void Substring_replacer_replaces_all_occurrences()
    {
        var step = new ReplacerStep(ReplacerKind.FindReplace, "http://", "https://");
        var payload = new ClickPayload("http://example.com");
        step.Process(payload);
        Assert.Equal("https://example.com", payload.Url);
    }

    [Fact]
    public void Regex_replacer_supports_groups()
    {
        var step = new ReplacerStep(ReplacerKind.Regex, @"twitter\.com", "x.com");
        var payload = new ClickPayload("https://twitter.com/a");
        step.Process(payload);
        Assert.Equal("https://x.com/a", payload.Url);
    }

    [Fact]
    public void Invalid_regex_leaves_url_untouched()
    {
        var step = new ReplacerStep(ReplacerKind.Regex, "[", "x");
        var payload = new ClickPayload("https://example.com");
        step.Process(payload);
        Assert.Equal("https://example.com", payload.Url);
    }

    [Theory]
    [InlineData("substr|find|repl", ReplacerKind.FindReplace, "find", "repl")]
    [InlineData("rgx|f.*|r", ReplacerKind.Regex, "f.*", "r")]
    public void Parse_bt_replacer_format(string line, ReplacerKind kind, string find, string replace)
    {
        var step = ReplacerStep.Parse(line);
        Assert.Equal(kind, step.Kind);
        Assert.Equal(find, step.Find);
        Assert.Equal(replace, step.Replace);
        Assert.Equal(line, step.Serialize());
    }

    [Fact]
    public void Pipeline_runs_steps_in_order()
    {
        var pipeline = new UrlPipeline()
            .Add(new ReplacerStep(ReplacerKind.FindReplace, "a.com", "b.com"))
            .Add(new ReplacerStep(ReplacerKind.FindReplace, "b.com", "c.com"));
        var payload = new ClickPayload("https://a.com");
        pipeline.Process(payload);
        Assert.Equal("https://c.com", payload.Url);
    }

    [Fact]
    public void Safelinks_step_decodes_embedded_destination()
    {
        var step = new SafeLinksStep();
        var payload = new ClickPayload("https://nam01.safelinks.protection.outlook.com/?url=https%3A%2F%2Fgithub.com%2Fdamianh%2Fbrowser-wrangler&data=123");

        step.Process(payload);

        Assert.Equal("https://github.com/damianh/browser-wrangler", payload.Url);
    }

    [Fact]
    public void Safelinks_step_ignores_invalid_or_unsupported_destination()
    {
        var step = new SafeLinksStep();
        var invalidPayload = new ClickPayload("https://nam01.safelinks.protection.outlook.com/?url=not-a-url");
        var mailtoPayload = new ClickPayload("https://nam01.safelinks.protection.outlook.com/?url=mailto%3Atest%40example.com");

        step.Process(invalidPayload);
        step.Process(mailtoPayload);

        Assert.Equal("https://nam01.safelinks.protection.outlook.com/?url=not-a-url", invalidPayload.Url);
        Assert.Equal("https://nam01.safelinks.protection.outlook.com/?url=mailto%3Atest%40example.com", mailtoPayload.Url);
    }

    [Fact]
    public void Redirect_expander_follows_redirect_chain()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.MovedPermanently)
            {
                Headers = { Location = new Uri("https://example.com/final") },
            },
            new HttpResponseMessage(HttpStatusCode.OK)));
        var step = new RedirectExpandStep(httpClient);
        var payload = new ClickPayload("https://bit.ly/example");

        step.Process(payload);

        Assert.Equal("https://example.com/final", payload.Url);
    }

    [Fact]
    public void Redirect_expander_treats_case_distinct_targets_as_unique_hops()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri("https://example.com/Path") },
            },
            new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri("https://example.com/path") },
            },
            new HttpResponseMessage(HttpStatusCode.OK)));
        var step = new RedirectExpandStep(httpClient);
        var payload = new ClickPayload("https://short.example/case-sensitive");

        step.Process(payload);

        Assert.Equal("https://example.com/path", payload.Url);
    }

    [Fact]
    public void Redirect_expander_ignores_unsupported_redirect_targets()
    {
        using var mailtoLocationClient = new HttpClient(new StubHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri("mailto:test@example.com") },
            }));
        var mailtoLocationStep = new RedirectExpandStep(mailtoLocationClient);
        var mailtoLocationPayload = new ClickPayload("https://short.example/mailto");

        mailtoLocationStep.Process(mailtoLocationPayload);

        Assert.Equal("https://short.example/mailto", mailtoLocationPayload.Url);
    }

    [Fact]
    public void Redirect_expander_retries_with_get_when_head_not_supported()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.MethodNotAllowed),
            new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri("https://example.com/final") },
            },
            new HttpResponseMessage(HttpStatusCode.OK)));
        var step = new RedirectExpandStep(httpClient);
        var payload = new ClickPayload("https://t.co/example");

        step.Process(payload);

        Assert.Equal("https://example.com/final", payload.Url);
    }

    [Fact]
    public void Redirect_expander_aborts_when_total_resolution_budget_expires()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(
            DelayedResponse(
                TimeSpan.FromSeconds(2),
                new HttpResponseMessage(HttpStatusCode.Redirect)
                {
                    Headers = { Location = new Uri("https://example.com/one") },
                }),
            DelayedResponse(
                TimeSpan.FromSeconds(2),
                new HttpResponseMessage(HttpStatusCode.Redirect)
                {
                    Headers = { Location = new Uri("https://example.com/two") },
                }),
            DelayedResponse(
                TimeSpan.FromSeconds(2),
                new HttpResponseMessage(HttpStatusCode.OK))));
        var step = new RedirectExpandStep(httpClient);
        var payload = new ClickPayload("https://short.example/slow");

        step.Process(payload);

        Assert.Equal("https://short.example/slow", payload.Url);
    }
}
