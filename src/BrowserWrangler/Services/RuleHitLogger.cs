using BrowserWrangler.Core.Configuration;
using BrowserWrangler.Core.Launching;
using BrowserWrangler.Core.Logging;
using BrowserWrangler.Core.Models;
using BrowserWrangler.Core.Rules;

namespace BrowserWrangler.Services;

internal static class RuleHitLogger
{
    private static readonly RuleHitLogStore Store = new();

    public static void LogDirectOpen(AppConfig config, ClickPayload payload, BrowserMatchResult match)
    {
        if (!config.LogRuleHits)
        {
            return;
        }

        Store.Append(ToEntry(payload, match.Profile, match.Rule, source: "open"));
    }

    public static void LogPickerSelection(AppConfig config, RouteDecision decision, BrowserProfile selectedProfile)
    {
        if (!config.LogRuleHits)
        {
            return;
        }

        BrowserMatchResult? selectedMatch = decision.Matches.FirstOrDefault(m => m.Profile.LongId == selectedProfile.LongId);
        MatchRule? rule = selectedMatch?.Rule;
        Store.Append(ToEntry(decision.Payload, selectedProfile, rule, source: "picker"));
    }

    private static RuleHitLogEntry ToEntry(ClickPayload payload, BrowserProfile profile, MatchRule? rule, string source)
    {
        bool isFallback = rule?.IsFallback == true;
        string ruleText = rule switch
        {
            null => "(picker override)",
            { IsFallback: true } => "(default fallback)",
            _ => rule.ToLine(),
        };

        return new RuleHitLogEntry
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            Url = payload.Url,
            ProfileLongId = profile.LongId,
            ProfileDisplayName = profile.BestDisplayName,
            RuleText = ruleText,
            IsFallback = isFallback,
            Source = source,
        };
    }
}
