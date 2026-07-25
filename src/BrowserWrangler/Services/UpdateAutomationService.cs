using System.Net;
using System.Runtime.InteropServices;
using BrowserWrangler.Core.Configuration;
using BrowserWrangler.Core.Updates;

namespace BrowserWrangler.Services;

public sealed record UpdateAutomationSnapshot
{
    public bool IsChecking { get; init; }
    public bool IsDownloading { get; init; }
    public string StatusMessage { get; init; } = string.Empty;
    public UpdateCheckResult? LastCheckResult { get; init; }
    public string PendingInstallerPath { get; init; } = string.Empty;
    public string PendingInstallerVersion { get; init; } = string.Empty;
}

/// <summary>
/// Background update automation while the config app is open.
/// Checks stable releases and optionally downloads a trusted installer asset.
/// </summary>
public sealed class UpdateAutomationService : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _snapshotLock = new();
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private UpdateAutomationSnapshot _snapshot = new();

    public event EventHandler? SnapshotChanged;

    public UpdateAutomationSnapshot Snapshot
    {
        get
        {
            lock (_snapshotLock)
            {
                return _snapshot;
            }
        }
    }

    public void Start()
    {
        if (_loopCts is not null)
        {
            return;
        }

        AppConfig config = AppState.Config;
        SetSnapshot(new UpdateAutomationSnapshot
        {
            PendingInstallerPath = config.Updates.PendingInstallerPath,
            PendingInstallerVersion = config.Updates.PendingInstallerVersion,
        });

        _loopCts = new CancellationTokenSource();
        _loopTask = RunLoopAsync(_loopCts.Token);
        _ = CheckNowAsync();
    }

    public void Stop()
    {
        CancellationTokenSource? cts = _loopCts;
        _loopCts = null;
        if (cts is null)
        {
            return;
        }

        cts.Cancel();
        cts.Dispose();
        _loopTask = null;
    }

    public async Task<UpdateCheckResult> CheckNowAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SetSnapshot(Snapshot with { IsChecking = true, StatusMessage = "Checking for updates…" });

            using HttpClient client = UpdateChecker.CreateHttpClient(DiagnosticsInfo.AppVersion);
            var checker = new UpdateChecker(client);
            string arch = RuntimeInformation.ProcessArchitecture.ToString();
            UpdateCheckResult result = await checker
                .CheckAsync(DiagnosticsInfo.AppVersion, arch, cancellationToken)
                .ConfigureAwait(false);

            AppConfig config = AppState.Config;
            config.Updates.LastCheckUtc = DateTimeOffset.UtcNow.ToString("O");
            AppState.Save();

            var snapshot = Snapshot with
            {
                IsChecking = false,
                LastCheckResult = result,
                StatusMessage = result.Message,
                PendingInstallerPath = config.Updates.PendingInstallerPath,
                PendingInstallerVersion = config.Updates.PendingInstallerVersion,
            };
            SetSnapshot(snapshot);

            if (result.Status == UpdateCheckStatus.UpdateAvailable
                && config.Updates.AutoDownloadInstaller
                && result.InstallerDownloadUrl is { Length: > 0 })
            {
                await DownloadInstallerIfNeededAsync(result, cancellationToken).ConfigureAwait(false);
            }

            return result;
        }
        finally
        {
            SetSnapshot(Snapshot with { IsChecking = false });
            _gate.Release();
        }
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            AppConfig config = AppState.Config;
            if (!config.Updates.AutoCheckEnabled)
            {
                await Task.Delay(TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false);
                continue;
            }

            int hours = Math.Clamp(config.Updates.CheckIntervalHours, 1, 168);
            await Task.Delay(TimeSpan.FromHours(hours), cancellationToken).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await CheckNowAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task DownloadInstallerIfNeededAsync(UpdateCheckResult result, CancellationToken cancellationToken)
    {
        Version? latest = result.LatestVersion;
        if (latest is null || result.InstallerDownloadUrl is not { Length: > 0 } downloadUrl)
        {
            return;
        }

        AppConfig config = AppState.Config;
        string expectedVersion = latest.ToString();
        if (string.Equals(config.Updates.PendingInstallerVersion, expectedVersion, StringComparison.Ordinal)
            && File.Exists(config.Updates.PendingInstallerPath))
        {
            return;
        }

        SetSnapshot(Snapshot with { IsDownloading = true, StatusMessage = $"Downloading installer for {latest}…" });

        string updatesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BrowserWrangler",
            "updates");
        Directory.CreateDirectory(updatesDir);

        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out Uri? requestedUri) || !UpdateChecker.IsTrustedReleaseAssetUri(requestedUri))
        {
            SetSnapshot(Snapshot with { IsDownloading = false, StatusMessage = "Update download URL is not trusted." });
            return;
        }

        using HttpClient client = UpdateChecker.CreateHttpClient(DiagnosticsInfo.AppVersion, TimeSpan.FromMinutes(3));
        using HttpResponseMessage response = await client
            .GetAsync(requestedUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode is not HttpStatusCode.OK)
        {
            SetSnapshot(Snapshot with { IsDownloading = false, StatusMessage = $"Download failed: {(int)response.StatusCode} {response.ReasonPhrase}." });
            return;
        }

        Uri? finalUri = response.RequestMessage?.RequestUri;
        if (finalUri is null || !UpdateChecker.IsTrustedReleaseAssetUri(finalUri))
        {
            SetSnapshot(Snapshot with { IsDownloading = false, StatusMessage = "Download redirect target is not trusted." });
            return;
        }

        string fileName = Path.GetFileName(finalUri.LocalPath);
        if (!fileName.EndsWith("-setup.exe", StringComparison.OrdinalIgnoreCase))
        {
            SetSnapshot(Snapshot with { IsDownloading = false, StatusMessage = "Downloaded file is not a setup executable." });
            return;
        }

        string destination = Path.Combine(updatesDir, fileName);
        string temporary = destination + ".tmp";

        await using (FileStream target = File.Create(temporary))
        await using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        {
            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporary, destination, overwrite: true);

        config.Updates.PendingInstallerPath = destination;
        config.Updates.PendingInstallerVersion = expectedVersion;
        AppState.Save();

        SetSnapshot(Snapshot with
        {
            IsDownloading = false,
            StatusMessage = $"Downloaded version {latest}. Ready to install.",
            PendingInstallerPath = destination,
            PendingInstallerVersion = expectedVersion,
        });
    }

    private void SetSnapshot(UpdateAutomationSnapshot snapshot)
    {
        lock (_snapshotLock)
        {
            _snapshot = snapshot;
        }

        SnapshotChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        Stop();
        _gate.Dispose();
    }
}
