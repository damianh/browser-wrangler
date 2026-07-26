using System.Text.Json.Serialization;

namespace BrowserWrangler.Core.Logging;

/// <summary>Source-generated JSON contract for <see cref="RuleHitLogEntry"/> so the
/// rule-hit log keeps working under assembly trimming.</summary>
[JsonSerializable(typeof(RuleHitLogEntry))]
internal sealed partial class RuleHitLogJsonContext : JsonSerializerContext;
