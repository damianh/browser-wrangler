using System.Text;

namespace BrowserWrangler.Core.Launching;

/// <summary>
/// Quotes and splits command lines using the rules .NET applies to
/// <see cref="System.Diagnostics.ProcessStartInfo.Arguments"/> on every platform.
/// </summary>
public static class CommandLine
{
    public static string Quote(string argument)
    {
        if (argument.Length > 0 && !argument.Any(c => char.IsWhiteSpace(c) || c == '"'))
        {
            return argument;
        }

        var sb = new StringBuilder("\"");
        int backslashes = 0;
        foreach (char c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                sb.Append('\\', (backslashes * 2) + 1).Append('"');
            }
            else
            {
                sb.Append('\\', backslashes).Append(c);
            }

            backslashes = 0;
        }

        sb.Append('\\', backslashes * 2).Append('"');
        return sb.ToString();
    }

    public static string Join(IEnumerable<string> arguments) => string.Join(' ', arguments.Select(Quote));

    /// <summary>Inverse of <see cref="Join"/>.</summary>
    public static List<string> Split(string commandLine)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;
        bool hasToken = false;

        for (int i = 0; i < commandLine.Length; i++)
        {
            char c = commandLine[i];
            if (c == '\\')
            {
                int start = i;
                while (i < commandLine.Length && commandLine[i] == '\\')
                {
                    i++;
                }

                int count = i - start;
                if (i < commandLine.Length && commandLine[i] == '"')
                {
                    current.Append('\\', count / 2);
                    if (count % 2 == 1)
                    {
                        current.Append('"');
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else
                {
                    current.Append('\\', count);
                    i--;
                }

                hasToken = true;
                continue;
            }

            if (c == '"')
            {
                inQuotes = !inQuotes;
                hasToken = true;
                continue;
            }

            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (hasToken)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }

                continue;
            }

            current.Append(c);
            hasToken = true;
        }

        if (hasToken)
        {
            result.Add(current.ToString());
        }

        return result;
    }
}
