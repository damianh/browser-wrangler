namespace BrowserWrangler.Core.Models;

using System.Text.Json.Serialization;

public enum BrowserEngine
{
    Unknown,
    Chromium,
    Gecko,
}

/// <summary>
/// An installed (or user-defined) browser. Contains one or more profiles.
/// </summary>
public sealed class Browser
{
    public const string UwpCmdPrefix = "shell:AppsFolder\\";

    public Browser() { }

    public Browser(string id, string name, string openCommand)
    {
        Id = id;
        Name = name;
        OpenCommand = openCommand;
    }

    /// <summary>Stable identifier (registry key name for discovered browsers, GUID for custom ones).</summary>
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Path to the browser executable (or UWP shell command).</summary>
    public string OpenCommand { get; set; } = string.Empty;

    public BrowserEngine Engine { get; set; } = BrowserEngine.Unknown;

    public int SortOrder { get; set; }

    /// <summary>True when discovered from the system rather than user-defined.</summary>
    public bool IsAutoDiscovered { get; set; }

    public bool IsHidden { get; set; }

    /// <summary>Overrides the icon extracted from the executable when set.</summary>
    public string IconPath { get; set; } = string.Empty;

    /// <summary>Where the browser stores its data (profiles etc.).</summary>
    public string DataPath { get; set; } = string.Empty;

    public List<BrowserProfile> Profiles { get; set; } = [];

    [JsonIgnore]
    public bool IsWellKnown => Engine != BrowserEngine.Unknown;

    [JsonIgnore]
    public bool IsStoreApp => OpenCommand.StartsWith(UwpCmdPrefix, StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool SupportsFramelessWindows => Engine == BrowserEngine.Chromium;

    [JsonIgnore]
    public int TotalRuleCount => Profiles.Sum(p => p.Rules.Count);

    [JsonIgnore]
    public string BestIconPath => IconPath.Length > 0 ? IconPath : OpenCommand;

    public bool ContainsProfile(string longId) => Profiles.Any(p => p.LongId == longId);

    public override string ToString() => $"{Name} ({Id})";
}
