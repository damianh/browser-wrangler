using System.Buffers;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
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
    private const long MaxInstallerSizeBytes = 512L * 1024 * 1024;
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(3);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _snapshotLock = new();
    private readonly object _loopSignalLock = new();
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private TaskCompletionSource _loopSignal = NewLoopSignal();
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

            string pendingInstallerPath = string.Empty;
            string pendingInstallerVersion = string.Empty;
            string? persistenceFailure = null;
            try
            {
                AppState.MutateAndSave(config =>
                {
                    config.Updates.LastCheckUtc = DateTimeOffset.UtcNow.ToString("O");
                    pendingInstallerPath = config.Updates.PendingInstallerPath;
                    pendingInstallerVersion = config.Updates.PendingInstallerVersion;
                });
            }
            catch (IOException ex)
            {
                persistenceFailure = ex.Message;
            }
            catch (UnauthorizedAccessException ex)
            {
                persistenceFailure = ex.Message;
            }

            if (persistenceFailure is not null)
            {
                (pendingInstallerPath, pendingInstallerVersion) = AppState.ReadConfig(config =>
                    (config.Updates.PendingInstallerPath, config.Updates.PendingInstallerVersion));
            }

            string statusMessage = persistenceFailure is null
                ? result.Message
                : $"{result.Message} (Could not save update state: {persistenceFailure})";

            var snapshot = Snapshot with
            {
                IsChecking = false,
                LastCheckResult = result,
                StatusMessage = statusMessage,
                PendingInstallerPath = pendingInstallerPath,
                PendingInstallerVersion = pendingInstallerVersion,
            };
            SetSnapshot(snapshot);

            if (result.Status == UpdateCheckStatus.UpdateAvailable
                && AppState.ReadConfig(config => config.Updates.AutoDownloadInstaller)
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

    public void NotifyScheduleChanged()
    {
        TaskCompletionSource signal;
        lock (_loopSignalLock)
        {
            signal = _loopSignal;
            _loopSignal = NewLoopSignal();
        }

        _ = signal.TrySetResult();
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            AppConfig config = AppState.Config;
            if (!config.Updates.AutoCheckEnabled)
            {
                await WaitForDelayOrSettingsChangeAsync(TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false);
                continue;
            }

            TimeSpan delay = GetDelayUntilNextCheck(config);
            if (delay > TimeSpan.Zero)
            {
                await WaitForDelayOrSettingsChangeAsync(delay, cancellationToken).ConfigureAwait(false);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (!AppState.Config.Updates.AutoCheckEnabled)
            {
                continue;
            }

            try
            {
                await CheckNowAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException ex)
            {
                SetSnapshot(Snapshot with { StatusMessage = $"Update automation persistence failed: {ex.Message}" });
            }
            catch (UnauthorizedAccessException ex)
            {
                SetSnapshot(Snapshot with { StatusMessage = $"Update automation persistence failed: {ex.Message}" });
            }
        }
    }

    private static TimeSpan GetDelayUntilNextCheck(AppConfig config)
    {
        int hours = Math.Clamp(config.Updates.CheckIntervalHours, 1, 168);
        TimeSpan checkInterval = TimeSpan.FromHours(hours);
        string lastCheckUtc = config.Updates.LastCheckUtc;
        if (!DateTimeOffset.TryParse(lastCheckUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed))
        {
            return TimeSpan.Zero;
        }

        TimeSpan elapsed = DateTimeOffset.UtcNow - parsed.ToUniversalTime();
        if (elapsed >= checkInterval)
        {
            return TimeSpan.Zero;
        }

        return checkInterval - elapsed;
    }

    private async Task WaitForDelayOrSettingsChangeAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        Task settingsChanged;
        lock (_loopSignalLock)
        {
            settingsChanged = _loopSignal.Task;
        }

        Task delayTask = Task.Delay(delay, cancellationToken);
        _ = await Task.WhenAny(delayTask, settingsChanged).ConfigureAwait(false);

        if (delayTask.IsCompleted)
        {
            await delayTask.ConfigureAwait(false);
        }
    }

    private async Task DownloadInstallerIfNeededAsync(UpdateCheckResult result, CancellationToken cancellationToken)
    {
        Version? latest = result.LatestVersion;
        if (latest is null || result.InstallerDownloadUrl is not { Length: > 0 } downloadUrl)
        {
            return;
        }

        string expectedVersion = latest.ToString();
        if (AppState.ReadConfig(config =>
                string.Equals(config.Updates.PendingInstallerVersion, expectedVersion, StringComparison.Ordinal)
                && File.Exists(config.Updates.PendingInstallerPath)))
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

        string? temporary = null;
        try
        {
            using var downloadTimeoutCts = new CancellationTokenSource(DownloadTimeout);
            using var downloadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, downloadTimeoutCts.Token);
            CancellationToken downloadToken = downloadCts.Token;

            using HttpClient client = UpdateChecker.CreateHttpClient(DiagnosticsInfo.AppVersion, DownloadTimeout);
            using HttpResponseMessage response = await client
                .GetAsync(requestedUri, HttpCompletionOption.ResponseHeadersRead, downloadToken)
                .ConfigureAwait(false);
            if (response.StatusCode is not HttpStatusCode.OK)
            {
                SetSnapshot(Snapshot with { StatusMessage = $"Download failed: {(int)response.StatusCode} {response.ReasonPhrase}." });
                return;
            }

            Uri? finalUri = response.RequestMessage?.RequestUri;
            if (finalUri is null || !UpdateChecker.IsTrustedReleaseAssetUri(finalUri))
            {
                SetSnapshot(Snapshot with { StatusMessage = "Download redirect target is not trusted." });
                return;
            }

            string fileName = GetInstallerFileName(requestedUri, response.Content.Headers.ContentDisposition);
            if (!fileName.EndsWith("-setup.exe", StringComparison.OrdinalIgnoreCase))
            {
                SetSnapshot(Snapshot with { StatusMessage = "Downloaded file is not a setup executable." });
                return;
            }

            if (response.Content.Headers.ContentLength is long contentLength && contentLength > MaxInstallerSizeBytes)
            {
                SetSnapshot(Snapshot with { StatusMessage = "Installer download exceeds maximum allowed size." });
                return;
            }

            string destination = Path.Combine(updatesDir, fileName);
            temporary = destination + ".tmp";

            await using (FileStream target = File.Create(temporary))
            await using (Stream source = await response.Content.ReadAsStreamAsync(downloadToken).ConfigureAwait(false))
            {
                byte[] buffer = ArrayPool<byte>.Shared.Rent(81920);
                try
                {
                    long totalBytesRead = 0;
                    while (true)
                    {
                        int bytesRead = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), downloadToken).ConfigureAwait(false);
                        if (bytesRead == 0)
                        {
                            break;
                        }

                        totalBytesRead += bytesRead;
                        if (totalBytesRead > MaxInstallerSizeBytes)
                        {
                            SetSnapshot(Snapshot with { StatusMessage = "Installer download exceeds maximum allowed size." });
                            return;
                        }

                        await target.WriteAsync(buffer.AsMemory(0, bytesRead), downloadToken).ConfigureAwait(false);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }

            File.Move(temporary, destination, overwrite: true);
            temporary = null;

            AppState.MutateAndSave(config =>
            {
                config.Updates.PendingInstallerPath = destination;
                config.Updates.PendingInstallerVersion = expectedVersion;
            });

            SetSnapshot(Snapshot with
            {
                StatusMessage = $"Downloaded version {latest}. Ready to install.",
                PendingInstallerPath = destination,
                PendingInstallerVersion = expectedVersion,
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            SetSnapshot(Snapshot with { StatusMessage = "Installer download timed out." });
        }
        catch (HttpRequestException ex)
        {
            SetSnapshot(Snapshot with { StatusMessage = $"Installer download failed: {ex.Message}" });
        }
        catch (IOException ex)
        {
            SetSnapshot(Snapshot with { StatusMessage = $"Installer download failed: {ex.Message}" });
        }
        catch (UnauthorizedAccessException ex)
        {
            SetSnapshot(Snapshot with { StatusMessage = $"Installer download failed: {ex.Message}" });
        }
        finally
        {
            if (temporary is { Length: > 0 } && File.Exists(temporary))
            {
                try
                {
                    File.Delete(temporary);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            (string pendingInstallerPath, string pendingInstallerVersion) = AppState.ReadConfig(config =>
                (config.Updates.PendingInstallerPath, config.Updates.PendingInstallerVersion));
            SetSnapshot(Snapshot with
            {
                IsDownloading = false,
                PendingInstallerPath = pendingInstallerPath,
                PendingInstallerVersion = pendingInstallerVersion,
            });
        }
    }

    private static string GetInstallerFileName(Uri requestedUri, ContentDispositionHeaderValue? contentDisposition)
    {
        string? contentDispositionName = contentDisposition?.FileNameStar ?? contentDisposition?.FileName;
        if (!string.IsNullOrWhiteSpace(contentDispositionName))
        {
            string candidate = Path.GetFileName(contentDispositionName.Trim().Trim('"'));
            if (candidate.Length > 0)
            {
                return candidate;
            }
        }

        return Path.GetFileName(requestedUri.LocalPath);
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

    private static TaskCompletionSource NewLoopSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
