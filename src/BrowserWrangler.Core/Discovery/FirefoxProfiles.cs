using BrowserWrangler.Core.Models;

namespace BrowserWrangler.Core.Discovery;

/// <summary>A classic profile parsed from Firefox's profiles.ini.</summary>
public sealed record FirefoxProfileInfo(
    string SectionId,
    string Name,
    string Path,
    bool IsRelative,
    string InstallationId);

/// <summary>
/// Discovers Firefox (Gecko) profiles from profiles.ini in the browser's data folder.
/// Classic profiles only; profile groups (sqlite) are a later milestone.
/// </summary>
public static class FirefoxProfiles
{
    /// <summary>Parses classic profiles from profiles.ini content.</summary>
    public static List<FirefoxProfileInfo> ParseProfilesIni(string iniContent)
    {
        // minimal ini parse: section -> key/value
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? current = null;
        foreach (string rawLine in iniContent.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                current = [];
                sections[line[1..^1]] = current;
                continue;
            }

            int eq = line.IndexOf('=');
            if (eq > 0 && current is not null)
            {
                current[line[..eq].Trim()] = line[(eq + 1)..].Trim();
            }
        }

        // map default profile path -> installation id from [Install...] sections
        var pathToInstall = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, Dictionary<string, string> values) in sections)
        {
            if (name.StartsWith("Install", StringComparison.OrdinalIgnoreCase) &&
                values.TryGetValue("Default", out string? def) && def.Length > 0)
            {
                pathToInstall[def] = name["Install".Length..];
            }
        }

        var result = new List<FirefoxProfileInfo>();
        foreach ((string name, Dictionary<string, string> values) in sections)
        {
            if (!name.StartsWith("Profile", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("ProfileGroups", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!values.TryGetValue("Path", out string? path))
            {
                continue;
            }

            // profile-group containers (StoreID) are not classic profiles
            if (values.ContainsKey("StoreID"))
            {
                continue;
            }

            string displayName = values.TryGetValue("Name", out string? n) ? n : name;
            bool isRelative = !values.TryGetValue("IsRelative", out string? rel) || rel == "1";
            string installId = pathToInstall.TryGetValue(path, out string? inst) ? inst : string.Empty;
            result.Add(new FirefoxProfileInfo(name, displayName, path, isRelative, installId));
        }

        return result;
    }

    /// <summary>Populates <paramref name="browser"/>.Profiles from its data folder.</summary>
    public static void Discover(Browser browser, string instanceId = "", bool includeClassicProfiles = true)
    {
        if (!browser.IsAutoDiscovered || browser.Engine != BrowserEngine.Gecko)
        {
            return;
        }

        string iniPath = Path.Combine(browser.DataPath, "profiles.ini");
        if (!File.Exists(iniPath))
        {
            return;
        }

        foreach (FirefoxProfileInfo info in ParseProfilesIni(File.ReadAllText(iniPath)))
        {
            // skip profiles bound to another Firefox installation
            if (info.InstallationId.Length > 0 && instanceId.Length > 0 && info.InstallationId != instanceId)
            {
                continue;
            }

            if (info.InstallationId.Length == 0 && !includeClassicProfiles)
            {
                continue;
            }

            string arg = $"\"{BrowserProfile.UrlArgName}\" -foreground -P \"{info.Name}\"";
            browser.Profiles.Add(new BrowserProfile(browser, info.SectionId, info.Name, arg)
            {
                SortOrder = browser.Profiles.Count,
            });
        }

        if (browser.Profiles.Count > 0)
        {
            browser.Profiles.Add(new BrowserProfile(
                browser, "private", "Private", $"\"{BrowserProfile.UrlArgName}\" -private-window")
            {
                IsIncognito = true,
                SortOrder = browser.Profiles.Count,
            });
        }
    }
}
