using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace BrowserWrangler.Core.Updates;

/// <summary>
/// Asks the GitHub Releases API whether a newer Browser Wrangler release has been
/// published. Network access is on-demand only; nothing is sent about the user.
/// </summary>
public sealed class UpdateChecker
{
    /// <summary>Version stamped into local (non-CI) builds by the csproj default.</summary>
    public static readonly Version DevelopmentVersion = new(1, 0, 0, 0);

    public const string DefaultLatestReleaseApiUrl = "https://api.github.com/repos/damianh/browser-wrangler/releases/latest";

    private const string UnreadableResponse = "GitHub returned a response that could not be understood.";

    private readonly HttpClient _httpClient;
    private readonly string _latestReleaseApiUrl;

    public UpdateChecker(HttpClient httpClient, string? latestReleaseApiUrl = null)
    {
        _httpClient = httpClient;
        _latestReleaseApiUrl = latestReleaseApiUrl ?? DefaultLatestReleaseApiUrl;
    }

    /// <summary>
    /// Creates an <see cref="HttpClient"/> configured the way the GitHub API expects.
    /// A User-Agent is mandatory; requests without one are rejected outright.
    /// </summary>
    public static HttpClient CreateHttpClient(Version appVersion, TimeSpan? timeout = null)
    {
        var client = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BrowserWrangler", appVersion.ToString()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public async Task<UpdateCheckResult> CheckAsync(Version currentVersion, CancellationToken cancellationToken = default)
    {
        string body;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _latestReleaseApiUrl);
            using HttpResponseMessage response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return UpdateCheckResult.Failed(DescribeFailure(response));
            }

            body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return UpdateCheckResult.Failed("The update check timed out. Check your connection and try again.");
        }
        catch (HttpRequestException ex)
        {
            return UpdateCheckResult.Failed($"Could not reach GitHub: {ex.Message}");
        }

        string? tag;
        string? releaseUrl;
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;

            // A syntactically valid payload can still have an unexpected shape (an array, a bare
            // value, or a numeric tag_name), so check the kind before reading anything out.
            if (root.ValueKind != JsonValueKind.Object || !TryReadString(root, "tag_name", out tag))
            {
                return UpdateCheckResult.Failed(UnreadableResponse);
            }

            // The release page link is optional decoration; a wrong type here should not fail the check.
            releaseUrl = TryReadString(root, "html_url", out string? url) ? url : null;
        }
        catch (JsonException)
        {
            return UpdateCheckResult.Failed(UnreadableResponse);
        }
        catch (InvalidOperationException)
        {
            return UpdateCheckResult.Failed(UnreadableResponse);
        }

        if (!TryParseVersionTag(tag, out Version? latestVersion))
        {
            return UpdateCheckResult.Failed($"The latest release tag ('{tag}') is not a recognisable version.");
        }

        if (Normalize(currentVersion) == DevelopmentVersion)
        {
            return UpdateCheckResult.DevelopmentBuild(latestVersion, releaseUrl);
        }

        return latestVersion > Normalize(currentVersion)
            ? UpdateCheckResult.UpdateAvailable(latestVersion, releaseUrl)
            : UpdateCheckResult.UpToDate(latestVersion, releaseUrl);
    }

    /// <summary>Reads a string property, treating a missing or wrongly typed value as absent.</summary>
    private static bool TryReadString(JsonElement owner, string propertyName, out string? value)
    {
        value = null;
        if (!owner.TryGetProperty(propertyName, out JsonElement element) || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString();
        return true;
    }

    /// <summary>Parses release tags such as "v2026.718.10" or "2026.718.10".</summary>
    public static bool TryParseVersionTag(string? tag, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        string trimmed = tag.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..];
        }

        if (!Version.TryParse(trimmed, out Version? parsed))
        {
            return false;
        }

        version = Normalize(parsed);
        return true;
    }

    /// <summary>Pads unspecified components to zero so two- and three-part versions compare sanely.</summary>
    private static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));

    private static string DescribeFailure(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            bool rateLimited = response.Headers.TryGetValues("x-ratelimit-remaining", out IEnumerable<string>? remaining)
                && remaining.FirstOrDefault() == "0";
            if (rateLimited)
            {
                return "GitHub's rate limit has been reached. Try again in a little while.";
            }
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return "No published releases were found.";
        }

        return $"GitHub returned {(int)response.StatusCode} {response.ReasonPhrase}.";
    }
}
