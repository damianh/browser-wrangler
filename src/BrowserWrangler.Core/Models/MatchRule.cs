using System.Text;
using System.Text.RegularExpressions;

namespace BrowserWrangler.Core.Models;

/// <summary>
/// A single URL matching rule attached to a browser profile.
/// Semantics mirror Browser Tamer's match_rule: case-insensitive substring by default,
/// full-string regex match when <see cref="IsRegex"/> is set.
/// </summary>
public sealed class MatchRule : IEquatable<MatchRule>
{
    private const string ScopeKey = "scope";
    private const string LocationKey = "loc";
    private const string PriorityKey = "priority";
    private const string ModeKey = "mode";
    private const string AppKey = "app";
    private const string TypeKey = "type";
    private const string TypeRegexKey = "regex";
    private const string WindowTitleKey = "window_title";
    private const string ProcessNameKey = "process_name";

    public MatchRule() { }

    public MatchRule(string value) => Value = value;

    public string Value { get; set; } = string.Empty;

    public MatchLocation Location { get; set; } = MatchLocation.Url;

    public MatchScope Scope { get; set; } = MatchScope.Any;

    /// <summary>Higher priority wins when multiple rules match.</summary>
    public int Priority { get; set; }

    public bool IsRegex { get; set; }

    /// <summary>Open the URL in app (frameless) mode when this rule matches.</summary>
    public bool AppMode { get; set; }

    /// <summary>Marks the fallback (default) rule; it always loses to real matches.</summary>
    public bool IsFallback { get; set; }

    /// <summary>
    /// Parses a rule from Browser Tamer's pipe-delimited line format,
    /// e.g. <c>scope:domain|priority:2|type:regex|value</c>.
    /// </summary>
    public static MatchRule Parse(string line)
    {
        var rule = new MatchRule();
        foreach (string part in line.Trim().Split('|'))
        {
            if (part.Length == 0)
            {
                continue;
            }

            int idx = part.IndexOf(':');
            if (idx < 0)
            {
                rule.Value = part;
                continue;
            }

            string key = part[..idx];
            string val = idx + 1 < part.Length ? part[(idx + 1)..] : string.Empty;
            switch (key)
            {
                case ScopeKey:
                    rule.Scope = ParseScope(val);
                    break;
                case LocationKey:
                    rule.Location = ParseLocation(val);
                    break;
                case PriorityKey:
                    rule.Priority = int.TryParse(val, out int p) ? p : 0;
                    break;
                case ModeKey:
                    if (val == AppKey)
                    {
                        rule.AppMode = true;
                    }

                    break;
                case TypeKey:
                    if (val == TypeRegexKey)
                    {
                        rule.IsRegex = true;
                    }

                    break;
                default:
                    // unknown key - treat whole part as the value (bt behaviour)
                    rule.Value = part;
                    break;
            }
        }

        return rule;
    }

    /// <summary>Serializes to Browser Tamer's pipe-delimited line format.</summary>
    public string ToLine()
    {
        string v = Value.Trim();
        if (v.Length == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        if (Location != MatchLocation.Url)
        {
            sb.Append(LocationKey).Append(':').Append(LocationToString(Location)).Append('|');
        }

        if (Scope != MatchScope.Any)
        {
            sb.Append(ScopeKey).Append(':').Append(ScopeToString(Scope)).Append('|');
        }

        if (Priority > 0)
        {
            sb.Append(PriorityKey).Append(':').Append(Priority).Append('|');
        }

        if (AppMode)
        {
            sb.Append(ModeKey).Append(':').Append(AppKey).Append('|');
        }

        if (IsRegex)
        {
            sb.Append(TypeKey).Append(':').Append(TypeRegexKey).Append('|');
        }

        sb.Append(v);
        return sb.ToString();
    }

    public bool IsMatch(ClickPayload payload)
    {
        if (string.IsNullOrEmpty(Value))
        {
            return false;
        }

        string src = Location switch
        {
            MatchLocation.Url => payload.Url,
            MatchLocation.WindowTitle => payload.WindowTitle,
            MatchLocation.ProcessName => payload.ProcessName,
            _ => string.Empty,
        };
        src = src.Trim();
        if (src.Length == 0)
        {
            return false;
        }

        if (Location != MatchLocation.Url)
        {
            return Contains(src, Value);
        }

        switch (Scope)
        {
            case MatchScope.Any:
                return Contains(src, Value);
            case MatchScope.Domain:
                ParseUrl(src, out _, out string host, out _);
                return Contains(host, Value);
            case MatchScope.Path:
                ParseUrl(src, out _, out _, out string path);
                return Contains(path, Value);
            default:
                return false;
        }
    }

    public bool IsMatch(string url) => IsMatch(new ClickPayload(url));

    /// <summary>Applies rule side effects (e.g. app mode) to the payload.</summary>
    public void ApplyTo(ClickPayload payload) => payload.AppMode = AppMode;

    /// <summary>
    /// Lightweight URL splitter matching bt's parse_url: no validation, just proto/host/path parts.
    /// </summary>
    public static void ParseUrl(string url, out string proto, out string host, out string path)
    {
        proto = host = path = string.Empty;
        const string protoEnd = "://";

        int idx = url.IndexOf(protoEnd, StringComparison.Ordinal);
        if (idx < 0)
        {
            host = url;
        }
        else
        {
            proto = url[..idx];
            host = idx + protoEnd.Length < url.Length ? url[(idx + protoEnd.Length)..] : string.Empty;
        }

        idx = host.IndexOf('/');
        if (idx >= 0)
        {
            path = host[(idx + 1)..];
            host = host[..idx];
        }
    }

    private bool Contains(string input, string value)
    {
        if (IsRegex)
        {
            try
            {
                // bt uses std::regex_match => entire input must match
                Match m = Regex.Match(input, value, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
                return m.Success && m.Index == 0 && m.Length == input.Length;
            }
            catch (ArgumentException)
            {
                return false; // invalid pattern
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }

        return input.Contains(value, StringComparison.OrdinalIgnoreCase);
    }

    public bool Equals(MatchRule? other) =>
        other is not null && Value == other.Value && Scope == other.Scope;

    public override bool Equals(object? obj) => Equals(obj as MatchRule);

    public override int GetHashCode() => HashCode.Combine(Value, Scope);

    public static string ScopeToString(MatchScope s) => s switch
    {
        MatchScope.Domain => "domain",
        MatchScope.Path => "path",
        _ => "any",
    };

    public static string LocationToString(MatchLocation l) => l switch
    {
        MatchLocation.WindowTitle => WindowTitleKey,
        MatchLocation.ProcessName => ProcessNameKey,
        _ => "url",
    };

    public static MatchScope ParseScope(string s) => s switch
    {
        "domain" => MatchScope.Domain,
        "path" => MatchScope.Path,
        _ => MatchScope.Any,
    };

    public static MatchLocation ParseLocation(string s) => s switch
    {
        WindowTitleKey => MatchLocation.WindowTitle,
        ProcessNameKey => MatchLocation.ProcessName,
        _ => MatchLocation.Url,
    };
}
