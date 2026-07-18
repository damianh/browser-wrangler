namespace BrowserWrangler.Core.Models;

using System.Text.Json.Serialization;

/// <summary>
/// A single launchable browser instance: a profile, incognito mode, or the browser itself
/// for single-profile browsers. Equivalent to bt's browser_instance.
/// </summary>
public sealed class BrowserProfile
{
    /// <summary>Placeholder replaced with the URL in launch arguments.</summary>
    public const string UrlArgName = "%url%";
    public const string UrlEncodedArgName = "%url_encoded%";

    public BrowserProfile() { }

    public BrowserProfile(Browser browser, string id, string name, string launchArg = "", string iconPath = "")
    {
        Browser = browser;
        Id = id;
        Name = name;
        LaunchArg = launchArg;
        IconPath = iconPath;
    }

    /// <summary>The browser this profile belongs to. Set on load; not serialized.</summary>
    [JsonIgnore]
    public Browser Browser { get; set; } = null!;

    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Arguments used to select this profile (e.g. <c>--profile-directory="Default"</c>).</summary>
    public string LaunchArg { get; set; } = string.Empty;

    /// <summary>Extra user-defined arguments appended after <see cref="LaunchArg"/>.</summary>
    public string UserArg { get; set; } = string.Empty;

    public List<MatchRule> Rules { get; set; } = [];

    public bool IsHidden { get; set; }

    /// <summary>Profile icon discovered from the browser's data, when known.</summary>
    public string IconPath { get; set; } = string.Empty;

    /// <summary>User override for the icon.</summary>
    public string UserIconPath { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public bool IsIncognito { get; set; }

    /// <summary>True if this is the browser's own default profile.</summary>
    public bool IsDefault { get; set; }

    /// <summary>Globally unique id: "browserId:profileId".</summary>
    [JsonIgnore]
    public string LongId => $"{Browser.Id}:{Id}";

    /// <summary>True when this is the only (non-incognito) profile of its browser.</summary>
    [JsonIgnore]
    public bool IsSingular =>
        !Browser.IsWellKnown || Browser.Profiles.Count(p => !p.IsIncognito) == 1;

    [JsonIgnore]
    public string BestDisplayName => IsSingular && !IsIncognito ? Browser.Name : $"{Browser.Name} - {Name}";

    public string GetBestIconPath(bool includeOverride = true)
    {
        if (includeOverride && UserIconPath.Length > 0)
        {
            return UserIconPath;
        }

        return IconPath.Length > 0 ? IconPath : Browser.BestIconPath;
    }

    /// <summary>Adds a rule from bt line format. Returns false when a duplicate exists.</summary>
    public bool AddRule(string ruleText)
    {
        MatchRule rule = MatchRule.Parse(ruleText);
        if (Rules.Contains(rule))
        {
            return false;
        }

        Rules.Add(rule);
        return true;
    }

    public override string ToString() => BestDisplayName;
}
