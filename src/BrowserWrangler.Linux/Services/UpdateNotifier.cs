using System.Globalization;
using System.Net.Http;
using BrowserWrangler.Core.Updates;

namespace BrowserWrangler.Linux.Services;

/// <summary>
/// Notify-only update checks: Linux installs are updated by the package manager, a new
/// AppImage or tarball, so we only point the user at the release page.
/// </summary>
internal static class UpdateNotifier
{
    public static async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        using HttpClient client = UpdateChecker.CreateHttpClient(DiagnosticsInfo.AppVersion);
        UpdateCheckResult result = await new UpdateChecker(client).CheckAsync(DiagnosticsInfo.AppVersion, cancellationToken: cancellationToken);
        if (result.Status != UpdateCheckStatus.Failed)
        {
            AppState.MutateAndSave(c => c.Updates.LastCheckUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        }

        return result;
    }
}
