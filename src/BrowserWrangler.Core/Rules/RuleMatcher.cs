using BrowserWrangler.Core.Models;

namespace BrowserWrangler.Core.Rules;

/// <summary>A profile that matched a payload, together with the winning rule.</summary>
public sealed record BrowserMatchResult(BrowserProfile Profile, MatchRule Rule);

/// <summary>
/// Matches click payloads against all browser profiles' rules. Mirrors bt's browser::match.
/// </summary>
public static class RuleMatcher
{
    /// <summary>
    /// Returns all matching profiles sorted by rule priority (descending).
    /// When nothing matches, returns a single fallback result pointing at the default profile.
    /// Returns an empty list only when no browsers exist at all.
    /// </summary>
    public static List<BrowserMatchResult> Match(
        IReadOnlyList<Browser> browsers,
        ClickPayload payload,
        string defaultProfileLongId = "")
    {
        var results = new List<BrowserMatchResult>();

        foreach (Browser b in browsers)
        {
            foreach (BrowserProfile p in b.Profiles)
            {
                // highest-priority matching rule of this profile wins
                MatchRule? best = null;
                foreach (MatchRule rule in p.Rules)
                {
                    if (rule.IsMatch(payload) && (best is null || rule.Priority > best.Priority))
                    {
                        best = rule;
                    }
                }

                if (best is not null)
                {
                    results.Add(new BrowserMatchResult(p, best));
                }
            }
        }

        if (results.Count == 0 && browsers.Count > 0)
        {
            var fallbackRule = new MatchRule("default") { IsFallback = true };
            BrowserProfile? fallback = GetDefault(browsers, defaultProfileLongId);
            if (fallback is not null)
            {
                results.Add(new BrowserMatchResult(fallback, fallbackRule));
            }
        }

        if (results.Count > 1)
        {
            results.Sort((a, b) => b.Rule.Priority.CompareTo(a.Rule.Priority));
        }

        return results;
    }

    /// <summary>
    /// Finds the configured default profile; falls back to the first profile of the first browser.
    /// </summary>
    public static BrowserProfile? GetDefault(IReadOnlyList<Browser> browsers, string defaultProfileLongId)
    {
        BrowserProfile? byId = FindProfileByLongId(browsers, defaultProfileLongId);
        if (byId is not null)
        {
            return byId;
        }

        return browsers.FirstOrDefault(b => b.Profiles.Count > 0)?.Profiles[0];
    }

    public static BrowserProfile? FindProfileByLongId(IReadOnlyList<Browser> browsers, string longId)
    {
        if (string.IsNullOrEmpty(longId))
        {
            return null;
        }

        return browsers.SelectMany(b => b.Profiles).FirstOrDefault(p => p.LongId == longId);
    }

    /// <summary>Flattens browsers into launchable profiles, skipping hidden ones by default.</summary>
    public static List<BrowserProfile> ToProfiles(IReadOnlyList<Browser> browsers, bool skipHidden = true)
    {
        return browsers
            .Where(b => !skipHidden || !b.IsHidden)
            .SelectMany(b => b.Profiles)
            .Where(p => !skipHidden || !p.IsHidden)
            .ToList();
    }
}
