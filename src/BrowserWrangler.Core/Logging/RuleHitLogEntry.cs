namespace BrowserWrangler.Core.Logging;

/// <summary>
/// A single persisted routing event for the rule-hit viewer.
/// </summary>
public sealed class RuleHitLogEntry
{
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;

    public string Url { get; set; } = string.Empty;

    public string ProfileLongId { get; set; } = string.Empty;

    public string ProfileDisplayName { get; set; } = string.Empty;

    public string RuleText { get; set; } = string.Empty;

    public bool IsFallback { get; set; }

    /// <summary>
    /// Origin of the decision ("open" for direct route, "picker" for user picker selection).
    /// </summary>
    public string Source { get; set; } = string.Empty;
}
