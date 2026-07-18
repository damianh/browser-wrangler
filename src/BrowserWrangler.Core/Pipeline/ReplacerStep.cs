using System.Text.RegularExpressions;
using BrowserWrangler.Core.Models;

namespace BrowserWrangler.Core.Pipeline;

public enum ReplacerKind
{
    FindReplace,
    Regex,
}

/// <summary>
/// Find/replace substitution pipeline step. Mirrors bt's replacer, including its
/// "kind|find|replace" pipe-delimited serialization ("substr" or "rgx" kind).
/// </summary>
public sealed class ReplacerStep : IUrlPipelineStep
{
    public ReplacerStep(ReplacerKind kind, string find, string replace)
    {
        Kind = kind;
        Find = find;
        Replace = replace;
    }

    public ReplacerKind Kind { get; }

    public string Find { get; }

    public string Replace { get; }

    public string Name => "Substitute";

    public static ReplacerStep Parse(string rule)
    {
        string[] parts = rule.Split('|');
        ReplacerKind kind = parts.Length > 0 && parts[0] == "rgx" ? ReplacerKind.Regex : ReplacerKind.FindReplace;
        string find = parts.Length == 3 ? parts[1] : string.Empty;
        string replace = parts.Length == 3 ? parts[2] : string.Empty;
        return new ReplacerStep(kind, find, replace);
    }

    public string Serialize() =>
        string.Join('|', Kind == ReplacerKind.FindReplace ? "substr" : "rgx", Find, Replace);

    public void Process(ClickPayload payload)
    {
        if (string.IsNullOrEmpty(Find))
        {
            return;
        }

        if (Kind == ReplacerKind.FindReplace)
        {
            payload.Url = payload.Url.Replace(Find, Replace, StringComparison.Ordinal);
        }
        else
        {
            try
            {
                payload.Url = Regex.Replace(
                    payload.Url, Find, Replace, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            }
            catch (ArgumentException)
            {
                // invalid pattern - leave URL untouched
            }
            catch (RegexMatchTimeoutException)
            {
            }
        }
    }
}
