using System.Text;

namespace BrowserWrangler.Core.Platform.Linux;

/// <summary>
/// The [Desktop Entry] group of a .desktop file
/// (https://specifications.freedesktop.org/desktop-entry-spec/).
/// </summary>
public sealed class DesktopEntry
{
    private static readonly HashSet<string> FieldCodes =
        ["%f", "%F", "%u", "%U", "%d", "%D", "%n", "%N", "%i", "%c", "%k", "%v", "%m"];

    private readonly Dictionary<string, string> _values;

    private DesktopEntry(string id, string path, Dictionary<string, string> values)
    {
        Id = id;
        FilePath = path;
        _values = values;
    }

    /// <summary>Desktop file id, e.g. "firefox.desktop" or "org.mozilla.firefox.desktop".</summary>
    public string Id { get; }

    public string FilePath { get; }

    public string Name => Get("Name");

    public string Exec => Get("Exec");

    public string Icon => Get("Icon");

    public string Type => Get("Type");

    public bool IsHidden => Get("Hidden").Equals("true", StringComparison.OrdinalIgnoreCase);

    public bool NoDisplay => Get("NoDisplay").Equals("true", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<string> MimeTypes =>
        Get("MimeType").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public string Get(string key) => _values.TryGetValue(key, out string? v) ? v : string.Empty;

    public static DesktopEntry? TryLoad(string id, string path)
    {
        try
        {
            return Parse(id, path, File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Parses the [Desktop Entry] group; returns null when the file has none.</summary>
    public static DesktopEntry? Parse(string id, string path, string content)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        bool inGroup = false;
        bool sawGroup = false;
        foreach (string rawLine in content.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                inGroup = trimmed == "[Desktop Entry]";
                sawGroup |= inGroup;
                continue;
            }

            int eq = line.IndexOf('=');
            if (!inGroup || eq <= 0)
            {
                continue;
            }

            string key = line[..eq].Trim();
            if (key.Contains('['))
            {
                continue; // localized variants
            }

            values.TryAdd(key, UnescapeString(line[(eq + 1)..].Trim()));
        }

        return sawGroup ? new DesktopEntry(id, path, values) : null;
    }

    /// <summary>
    /// Splits an Exec value into arguments, applying the spec's quoting rules and dropping field
    /// codes (%u, %F, ...) plus Flatpak's file-forwarding markers (@@u / @@).
    /// </summary>
    public static List<string> SplitExec(string exec)
    {
        var args = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;
        bool hasToken = false;
        bool quoted = false;

        void Flush()
        {
            if (!hasToken)
            {
                return;
            }

            string token = current.ToString();
            current.Clear();
            hasToken = false;
            bool wasQuoted = quoted;
            quoted = false;

            if (!wasQuoted && (FieldCodes.Contains(token) || token is "@@" or "@@u" or "@@f"))
            {
                return;
            }

            args.Add(wasQuoted ? token : ExpandPercents(token));
        }

        for (int i = 0; i < exec.Length; i++)
        {
            char c = exec[i];
            if (inQuotes)
            {
                if (c == '\\' && i + 1 < exec.Length && exec[i + 1] is '"' or '`' or '$' or '\\')
                {
                    current.Append(exec[++i]);
                }
                else if (c == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    current.Append(c);
                }

                continue;
            }

            if (c == '"')
            {
                inQuotes = true;
                hasToken = true;
                quoted = true;
            }
            else if (c is ' ' or '\t')
            {
                Flush();
            }
            else
            {
                current.Append(c);
                hasToken = true;
            }
        }

        Flush();
        return args;
    }

    /// <summary>Quotes a single argument for an Exec value (before string escaping).</summary>
    public static string QuoteExecArgument(string argument)
    {
        bool safe = argument.Length > 0 && argument.All(c =>
            char.IsAsciiLetterOrDigit(c) || c is '/' or '.' or '_' or '-' or '+' or ',' or ':' or '@');
        if (safe)
        {
            return argument;
        }

        var sb = new StringBuilder("\"");
        foreach (char c in argument)
        {
            if (c is '"' or '`' or '$' or '\\')
            {
                sb.Append('\\');
            }

            sb.Append(c);
        }

        return sb.Append('"').ToString();
    }

    /// <summary>Escapes a value for writing into a .desktop file.</summary>
    public static string EscapeString(string value) =>
        value.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\t", "\\t").Replace("\r", "\\r");

    private static string ExpandPercents(string token)
    {
        if (!token.Contains('%'))
        {
            return token;
        }

        var sb = new StringBuilder();
        for (int i = 0; i < token.Length; i++)
        {
            if (token[i] == '%' && i + 1 < token.Length)
            {
                if (token[i + 1] == '%')
                {
                    sb.Append('%');
                }

                i++; // drop embedded field codes
                continue;
            }

            sb.Append(token[i]);
        }

        return sb.ToString();
    }

    private static string UnescapeString(string value)
    {
        if (!value.Contains('\\'))
        {
            return value;
        }

        var sb = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                char next = value[++i];
                sb.Append(next switch
                {
                    's' => ' ',
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    '\\' => '\\',
                    _ => next,
                });
                if (next is not ('s' or 'n' or 't' or 'r' or '\\'))
                {
                    sb.Insert(sb.Length - 1, '\\');
                }

                continue;
            }

            sb.Append(value[i]);
        }

        return sb.ToString();
    }
}
