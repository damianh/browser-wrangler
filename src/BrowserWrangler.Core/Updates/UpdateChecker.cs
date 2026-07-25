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
    private static readonly string[] KnownInstallerArchitectureTokens =
    [
        "-x64-",
        "-arm64-",
        "-x86-",
        "-arm-",
    ];
    private static readonly HashSet<string> TrustedDownloadHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "github.com",
        "objects.githubusercontent.com",
        "github-releases.githubusercontent.com",
        "release-assets.githubusercontent.com",
    };

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

    public async Task<UpdateCheckResult> CheckAsync(
        Version currentVersion,
        string? preferredArchitecture = null,
        CancellationToken cancellationToken = default)
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
        string? installerDownloadUrl = null;
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
            if (!TryReadBoolean(root, "prerelease", out bool prerelease))
            {
                return UpdateCheckResult.Failed(UnreadableResponse);
            }

            if (prerelease)
            {
                return UpdateCheckResult.Failed("The newest release is marked as a prerelease; only stable releases are supported.");
            }

            installerDownloadUrl = FindTrustedInstallerAssetUrl(root, preferredArchitecture);
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
            ? UpdateCheckResult.UpdateAvailable(latestVersion, releaseUrl, installerDownloadUrl)
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

    private static bool TryReadBoolean(JsonElement owner, string propertyName, out bool value)
    {
        value = false;
        if (!owner.TryGetProperty(propertyName, out JsonElement element) || element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        value = element.GetBoolean();
        return true;
    }

    /// <summary>Validates release asset URLs before any automatic download is attempted.</summary>
    public static bool IsTrustedReleaseAssetUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || !TrustedDownloadHosts.Contains(uri.Host))
        {
            return false;
        }

        if (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            string path = uri.AbsolutePath;
            if (!path.StartsWith("/damianh/browser-wrangler/releases/download/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string? FindTrustedInstallerAssetUrl(JsonElement release, string? preferredArchitecture)
    {
        if (!release.TryGetProperty("assets", out JsonElement assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string archToken = NormalizeArch(preferredArchitecture);
        string? fallback = null;
        foreach (JsonElement asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object
                || !TryReadString(asset, "name", out string? name)
                || !TryReadString(asset, "browser_download_url", out string? url)
                || name is null
                || url is null)
            {
                continue;
            }

            if (!name.EndsWith("-setup.exe", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || !IsTrustedReleaseAssetUri(uri))
            {
                continue;
            }

            if (name.Contains($"-{archToken}-", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            if (IsArchitectureSpecificInstaller(name))
            {
                continue;
            }

            fallback ??= url;
        }

        return fallback;
    }

    private static bool IsArchitectureSpecificInstaller(string installerName)
    {
        foreach (string token in KnownInstallerArchitectureTokens)
        {
            if (installerName.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeArch(string? preferredArchitecture)
    {
        if (string.IsNullOrWhiteSpace(preferredArchitecture))
        {
            return "x64";
        }

        string lowered = preferredArchitecture.Trim().ToLowerInvariant();
        return lowered.Contains("arm", StringComparison.Ordinal) ? "arm64" : "x64";
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
