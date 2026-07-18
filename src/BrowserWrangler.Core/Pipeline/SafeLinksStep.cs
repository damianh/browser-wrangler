using System.Net;
using BrowserWrangler.Core.Models;

namespace BrowserWrangler.Core.Pipeline;

/// <summary>
/// Replaces Outlook Safelinks URLs with the decoded destination in their "url" query parameter.
/// </summary>
public sealed class SafeLinksStep : IUrlPipelineStep
{
    private const string SafeLinksSuffix = "safelinks.protection.outlook.com";

    public string Name => "Unwrap Safelinks";

    public void Process(ClickPayload payload)
    {
        if (!Uri.TryCreate(payload.Url, UriKind.Absolute, out Uri? uri) || !IsSafeLinksHost(uri.Host))
        {
            return;
        }

        string? encodedUrl = TryGetQueryParameter(uri.Query, "url");
        if (string.IsNullOrWhiteSpace(encodedUrl))
        {
            return;
        }

        string decodedUrl = WebUtility.UrlDecode(encodedUrl);
        if (Uri.TryCreate(decodedUrl, UriKind.Absolute, out Uri? destination) &&
            destination is not null &&
            IsSupportedUri(destination))
        {
            payload.Url = destination.AbsoluteUri;
        }
    }

    private static bool IsSafeLinksHost(string host) =>
        host.Equals(SafeLinksSuffix, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith("." + SafeLinksSuffix, StringComparison.OrdinalIgnoreCase);

    private static string? TryGetQueryParameter(string query, string name)
    {
        ReadOnlySpan<char> remaining = query.AsSpan().TrimStart('?');
        while (!remaining.IsEmpty)
        {
            int separatorIndex = remaining.IndexOf('&');
            ReadOnlySpan<char> segment = separatorIndex >= 0 ? remaining[..separatorIndex] : remaining;

            int equalsIndex = segment.IndexOf('=');
            ReadOnlySpan<char> key = equalsIndex >= 0 ? segment[..equalsIndex] : segment;
            if (key.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                ReadOnlySpan<char> value = equalsIndex >= 0 ? segment[(equalsIndex + 1)..] : [];
                return value.ToString();
            }

            if (separatorIndex < 0)
            {
                break;
            }

            remaining = remaining[(separatorIndex + 1)..];
        }

        return null;
    }

    private static bool IsSupportedUri(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
}
