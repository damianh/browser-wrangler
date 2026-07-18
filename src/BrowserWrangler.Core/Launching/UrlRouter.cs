using BrowserWrangler.Core.Configuration;
using BrowserWrangler.Core.Models;
using BrowserWrangler.Core.Pipeline;
using BrowserWrangler.Core.Rules;

namespace BrowserWrangler.Core.Launching;

/// <summary>What the router decided to do with a URL.</summary>
public enum RouteAction
{
    /// <summary>Open directly in the single matched profile.</summary>
    Open,

    /// <summary>Show the picker so the user chooses.</summary>
    ShowPicker,

    /// <summary>Nothing to do (no browsers configured).</summary>
    None,
}

public sealed record RouteDecision(
    RouteAction Action,
    ClickPayload Payload,
    IReadOnlyList<BrowserMatchResult> Matches);

/// <summary>
/// The core URL-open flow: pipeline -> rule match -> decide (open vs picker).
/// Pure logic; actual launching/UI is done by the caller.
/// </summary>
public static class UrlRouter
{
    public static UrlPipeline BuildPipeline(AppConfig config)
    {
        var pipeline = new UrlPipeline();
        if (config.Pipeline.Substitute)
        {
            foreach (string rule in config.Pipeline.Substitutions)
            {
                pipeline.Add(ReplacerStep.Parse(rule));
            }
        }

        return pipeline;
    }

    /// <summary>
    /// Runs the payload through the pipeline and rules, and decides whether to open
    /// directly or show the picker, honouring picker trigger settings.
    /// </summary>
    /// <param name="pickerRequested">True when the user held a picker hotkey (e.g. Ctrl+Shift).</param>
    public static RouteDecision Route(AppConfig config, ClickPayload payload, bool pickerRequested = false)
    {
        BuildPipeline(config).Process(payload);

        List<BrowserMatchResult> matches = RuleMatcher.Match(config.Browsers, payload, config.DefaultProfile);
        if (matches.Count == 0)
        {
            return new RouteDecision(RouteAction.None, payload, matches);
        }

        PickerSettings picker = config.Picker;
        bool showPicker =
            pickerRequested ||
            picker.Always ||
            (picker.OnConflict && matches.Count > 1) ||
            (picker.OnNoRule && matches.Count == 1 && matches[0].Rule.IsFallback);

        if (showPicker)
        {
            return new RouteDecision(RouteAction.ShowPicker, payload, matches);
        }

        matches[0].Rule.ApplyTo(payload);
        return new RouteDecision(RouteAction.Open, payload, matches);
    }
}
