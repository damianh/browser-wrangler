using System.Text.Json.Serialization;

namespace BrowserWrangler.Core.Configuration;

/// <summary>Source-generated JSON contract for <see cref="AppConfig"/> so config
/// (de)serialization keeps working under assembly trimming.</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppConfig))]
internal sealed partial class AppConfigJsonContext : JsonSerializerContext;
