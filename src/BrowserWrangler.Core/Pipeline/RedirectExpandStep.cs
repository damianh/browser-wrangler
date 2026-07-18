using System.Net;
using BrowserWrangler.Core.Models;

namespace BrowserWrangler.Core.Pipeline;

/// <summary>
/// Resolves redirecting HTTP(S) URLs before rule matching so rules can target the final destination.
/// </summary>
public sealed class RedirectExpandStep : IUrlPipelineStep
{
    private const int MaxRedirects = 10;
    private static readonly HttpClient SharedClient = CreateHttpClient();

    private readonly HttpClient _httpClient;

    public RedirectExpandStep()
        : this(SharedClient)
    {
    }

    public RedirectExpandStep(HttpClient httpClient) => _httpClient = httpClient;

    public string Name => "Expand shortened URLs";

    public void Process(ClickPayload payload)
    {
        if (TryResolveFinalUrl(payload.Url, out string? resolvedUrl) &&
            resolvedUrl is not null &&
            !string.Equals(payload.Url, resolvedUrl, StringComparison.Ordinal))
        {
            payload.Url = resolvedUrl;
        }
    }

    private bool TryResolveFinalUrl(string inputUrl, out string? resolvedUrl)
    {
        resolvedUrl = null;
        if (!Uri.TryCreate(inputUrl, UriKind.Absolute, out Uri? parsedUri) || parsedUri is null ||
            (parsedUri.Scheme != Uri.UriSchemeHttp && parsedUri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        Uri current = parsedUri;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { current.AbsoluteUri };

        try
        {
            for (int redirectCount = 0; redirectCount < MaxRedirects; redirectCount++)
            {
                using HttpResponseMessage response = Send(current, HttpMethod.Head);
                if (ShouldRetryWithGet(response.StatusCode))
                {
                    using HttpResponseMessage getResponse = Send(current, HttpMethod.Get);
                    if (!TryFollowRedirect(current, getResponse, visited, out Uri nextFromGet))
                    {
                        break;
                    }

                    current = nextFromGet;
                    continue;
                }

                if (!TryFollowRedirect(current, response, visited, out Uri next))
                {
                    break;
                }

                current = next;
            }
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }

        resolvedUrl = current.AbsoluteUri;
        return true;
    }

    private HttpResponseMessage Send(Uri uri, HttpMethod method)
    {
        using var request = new HttpRequestMessage(method, uri);
        return _httpClient.Send(request, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);
    }

    private static bool TryFollowRedirect(
        Uri current,
        HttpResponseMessage response,
        HashSet<string> visited,
        out Uri next)
    {
        next = null!;
        if (!IsRedirect(response.StatusCode) || response.Headers.Location is not Uri location)
        {
            return false;
        }

        Uri candidate = location.IsAbsoluteUri ? location : new Uri(current, location);
        if (!visited.Add(candidate.AbsoluteUri))
        {
            return false;
        }

        next = candidate;
        return true;
    }

    private static bool ShouldRetryWithGet(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented;

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.MultipleChoices or
        HttpStatusCode.MovedPermanently or
        HttpStatusCode.Redirect or
        HttpStatusCode.RedirectMethod or
        HttpStatusCode.TemporaryRedirect or
        HttpStatusCode.PermanentRedirect;

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
        })
        {
            Timeout = TimeSpan.FromSeconds(5),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BrowserWrangler/1.0");
        return client;
    }
}
