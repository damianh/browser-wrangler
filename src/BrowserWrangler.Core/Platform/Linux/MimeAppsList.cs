using System.Text;

namespace BrowserWrangler.Core.Platform.Linux;

/// <summary>
/// Reads and edits mimeapps.list default handlers
/// (https://specifications.freedesktop.org/mime-apps-spec/).
/// </summary>
public static class MimeAppsList
{
    private const string DefaultGroup = "[Default Applications]";

    /// <summary>mimeapps.list files in lookup order, highest priority first.</summary>
    public static IEnumerable<string> LookupFiles(XdgDirectories dirs)
    {
        IEnumerable<string> configDirs = new[] { dirs.ConfigHome }.Concat(dirs.ConfigDirs);
        IEnumerable<string> dataAppDirs = dirs.ApplicationDirs;
        foreach (string dir in configDirs.Concat(dataAppDirs))
        {
            foreach (string desktop in dirs.CurrentDesktops)
            {
                yield return Path.Combine(dir, $"{desktop}-mimeapps.list");
            }

            yield return Path.Combine(dir, "mimeapps.list");
        }
    }

    /// <summary>Returns the desktop ids listed as default for <paramref name="mimeType"/> in a file's content.</summary>
    public static IReadOnlyList<string> GetDefaults(string content, string mimeType)
    {
        bool inGroup = false;
        foreach (string rawLine in content.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.StartsWith('['))
            {
                inGroup = line == DefaultGroup;
                continue;
            }

            int eq = line.IndexOf('=');
            if (inGroup && eq > 0 && line[..eq].Trim() == mimeType)
            {
                return line[(eq + 1)..].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
        }

        return [];
    }

    /// <summary>
    /// Resolves the effective default handler for a mime type: the first installed desktop id in
    /// the highest-priority file that mentions the type.
    /// </summary>
    public static string QueryDefault(XdgDirectories dirs, string mimeType, Func<string, bool> isInstalled)
    {
        foreach (string file in LookupFiles(dirs))
        {
            string content;
            try
            {
                if (!File.Exists(file))
                {
                    continue;
                }

                content = File.ReadAllText(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string id in GetDefaults(content, mimeType))
            {
                if (isInstalled(id))
                {
                    return id;
                }
            }
        }

        return string.Empty;
    }

    /// <summary>Puts <paramref name="desktopId"/> first in the defaults for each mime type, keeping the rest as fallbacks.</summary>
    public static string SetDefault(string content, IEnumerable<string> mimeTypes, string desktopId) =>
        Edit(content, mimeTypes, existing => [desktopId, .. existing.Where(id => id != desktopId)]);

    /// <summary>Removes <paramref name="desktopId"/> from the defaults for each mime type.</summary>
    public static string RemoveDefault(string content, IEnumerable<string> mimeTypes, string desktopId) =>
        Edit(content, mimeTypes, existing => [.. existing.Where(id => id != desktopId)]);

    private static string Edit(string content, IEnumerable<string> mimeTypes, Func<IReadOnlyList<string>, List<string>> update)
    {
        var pending = new List<string>(mimeTypes);
        var lines = content.Replace("\r\n", "\n").Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        int groupStart = lines.FindIndex(l => l.Trim() == DefaultGroup);
        if (groupStart < 0)
        {
            if (lines.Count > 0 && lines[^1].Trim().Length > 0)
            {
                lines.Add(string.Empty);
            }

            lines.Add(DefaultGroup);
            groupStart = lines.Count - 1;
        }

        int groupEnd = lines.FindIndex(groupStart + 1, l => l.TrimStart().StartsWith('['));
        if (groupEnd < 0)
        {
            groupEnd = lines.Count;
        }

        for (int i = groupStart + 1; i < groupEnd; i++)
        {
            int eq = lines[i].IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            string key = lines[i][..eq].Trim();
            if (!pending.Remove(key))
            {
                continue;
            }

            List<string> ids = update(GetDefaults(DefaultGroup + "\n" + lines[i], key));
            if (ids.Count == 0)
            {
                lines.RemoveAt(i);
                i--;
                groupEnd--;
            }
            else
            {
                lines[i] = $"{key}={string.Join(';', ids)};";
            }
        }

        foreach (string mimeType in pending)
        {
            List<string> ids = update([]);
            if (ids.Count > 0)
            {
                lines.Insert(groupEnd++, $"{mimeType}={string.Join(';', ids)};");
            }
        }

        var sb = new StringBuilder();
        foreach (string line in lines)
        {
            sb.Append(line).Append('\n');
        }

        return sb.ToString();
    }
}
