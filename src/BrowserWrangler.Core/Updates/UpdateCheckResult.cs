namespace BrowserWrangler.Core.Updates;

/// <summary>Outcome of an <see cref="UpdateChecker"/> query.</summary>
public enum UpdateCheckStatus
{
    /// <summary>The running build is the newest published release.</summary>
    UpToDate,

    /// <summary>A newer release is published.</summary>
    UpdateAvailable,

    /// <summary>The running build was not produced by CI, so comparing it is meaningless.</summary>
    DevelopmentBuild,

    /// <summary>The check could not be completed.</summary>
    Failed,
}

/// <summary>Result of a single update check.</summary>
public sealed class UpdateCheckResult
{
    private UpdateCheckResult(UpdateCheckStatus status, string message, Version? latestVersion, string? releaseUrl)
    {
        Status = status;
        Message = message;
        LatestVersion = latestVersion;
        ReleaseUrl = releaseUrl;
    }

    public UpdateCheckStatus Status { get; }

    /// <summary>Human-readable summary suitable for display next to the check button.</summary>
    public string Message { get; }

    /// <summary>Newest published version, when it could be determined.</summary>
    public Version? LatestVersion { get; }

    /// <summary>Web page for the newest release, when it could be determined.</summary>
    public string? ReleaseUrl { get; }

    public static UpdateCheckResult UpToDate(Version latestVersion, string? releaseUrl) =>
        new(UpdateCheckStatus.UpToDate, $"You are running the latest version ({latestVersion}).", latestVersion, releaseUrl);

    public static UpdateCheckResult UpdateAvailable(Version latestVersion, string? releaseUrl) =>
        new(UpdateCheckStatus.UpdateAvailable, $"Version {latestVersion} is available.", latestVersion, releaseUrl);

    public static UpdateCheckResult DevelopmentBuild(Version? latestVersion, string? releaseUrl) =>
        new(UpdateCheckStatus.DevelopmentBuild, "This is a local development build, so there is nothing to compare against.", latestVersion, releaseUrl);

    public static UpdateCheckResult Failed(string message) =>
        new(UpdateCheckStatus.Failed, message, null, null);
}
