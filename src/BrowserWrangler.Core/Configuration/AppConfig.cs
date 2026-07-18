using System.Text.Json.Serialization;
using BrowserWrangler.Core.Models;

namespace BrowserWrangler.Core.Configuration;

/// <summary>When to show the picker window.</summary>
public sealed class PickerSettings
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool OnCtrlShift { get; set; } = true;
    public bool OnCtrlAlt { get; set; }
    public bool OnAltShift { get; set; }
    public bool OnCapsLock { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool OnConflict { get; set; } = true;
    public bool OnNoRule { get; set; }
    public bool Always { get; set; }
    public double IconSize { get; set; } = 32;
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool ShowKeyHints { get; set; } = true;
    public bool CloseOnFocusLoss { get; set; }
    public bool AlwaysOnTop { get; set; }
}

public sealed class ToastSettings
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool ShowOnOpen { get; set; } = true;
    public int VisibleSeconds { get; set; } = 3;
}

public sealed class PipelineSettings
{
    /// <summary>Decode Outlook Safelinks URLs before any other pipeline steps or rule matching.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool UnwrapSafelinks { get; set; } = true;

    /// <summary>
    /// Resolve redirecting URLs before matching. This makes a network request for clicked links and
    /// should stay opt-in because it adds latency and shares the destination with the redirect host.
    /// </summary>
    public bool ExpandShortenedUrls { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool Substitute { get; set; } = true;

    /// <summary>Substitution rules in bt format: "substr|find|replace" or "rgx|find|replace".</summary>
    public List<string> Substitutions { get; set; } = [];
}

/// <summary>Persisted size/position of the config window.</summary>
public sealed class WindowSettings
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int X { get; set; } = int.MinValue;
    public int Y { get; set; } = int.MinValue;
}

/// <summary>
/// Root application configuration, persisted as JSON.
/// </summary>
public sealed class AppConfig
{
    /// <summary>"", "light" or "dark".</summary>
    public string Theme { get; set; } = string.Empty;

    /// <summary>Long id ("browserId:profileId") of the fallback profile.</summary>
    public string DefaultProfile { get; set; } = string.Empty;

    public bool LogRuleHits { get; set; }

    public PickerSettings Picker { get; set; } = new();

    public ToastSettings Toast { get; set; } = new();

    public PipelineSettings Pipeline { get; set; } = new();

    public WindowSettings Window { get; set; } = new();

    public List<Browser> Browsers { get; set; } = [];
}
