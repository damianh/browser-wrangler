using System.Security.Cryptography;
using System.Text;
using BrowserWrangler.Core.Models;
using Microsoft.Data.Sqlite;

namespace BrowserWrangler.Core.Discovery;

/// <summary>A Firefox profile parsed from profiles.ini.</summary>
public sealed record FirefoxProfileInfo(
    string SectionId,
    string Name,
    string Path,
    bool IsRelative,
    string InstallationId,
    string StoreId = "");

/// <summary>
/// Discovers Firefox (Gecko) profiles from profiles.ini in the browser's data folder.
/// </summary>
public static class FirefoxProfiles
{
    /// <summary>Parses Firefox profiles from profiles.ini content.</summary>
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

            string displayName = values.TryGetValue("Name", out string? n) ? n : name;
            bool isRelative = !values.TryGetValue("IsRelative", out string? rel) || rel == "1";
            string installId = pathToInstall.TryGetValue(path, out string? inst) ? inst : string.Empty;
            string storeId = values.TryGetValue("StoreID", out string? sid) ? sid : string.Empty;
            result.Add(new FirefoxProfileInfo(name, displayName, path, isRelative, installId, storeId));
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

        foreach (FirefoxProfileInfo info in ResolveProfiles(browser.DataPath, ParseProfilesIni(File.ReadAllText(iniPath))))
        {
            // skip profiles bound to another Firefox installation
            if (info.InstallationId.Length > 0 && instanceId.Length > 0 && info.InstallationId != instanceId)
            {
                continue;
            }

            if (info.InstallationId.Length == 0 && info.StoreId.Length == 0 && !includeClassicProfiles)
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

    private static List<FirefoxProfileInfo> ResolveProfiles(string dataPath, List<FirefoxProfileInfo> iniProfiles)
    {
        var byPath = new Dictionary<string, FirefoxProfileInfo>(StringComparer.OrdinalIgnoreCase);
        var storeToInstall = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (FirefoxProfileInfo profile in iniProfiles)
        {
            string normalizedPath = NormalizeProfilePath(profile.Path);
            byPath[normalizedPath] = profile;

            if (profile.StoreId.Length > 0 && profile.InstallationId.Length > 0 &&
                !storeToInstall.ContainsKey(profile.StoreId))
            {
                storeToInstall[profile.StoreId] = profile.InstallationId;
            }
        }

        foreach (string storeId in iniProfiles
                     .Select(p => p.StoreId)
                     .Where(s => s.Length > 0)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (FirefoxStoreProfileInfo storeProfile in ReadStoreProfiles(dataPath, storeId))
            {
                string normalizedPath = NormalizeProfilePath(storeProfile.Path);
                if (byPath.TryGetValue(normalizedPath, out FirefoxProfileInfo? current))
                {
                    // Modern profile names come from the profile-group sqlite store.
                    byPath[normalizedPath] = current with
                    {
                        Name = storeProfile.Name,
                    };
                }
                else
                {
                    string syntheticId = BuildSyntheticProfileId(storeId, normalizedPath);
                    string installId = storeToInstall.GetValueOrDefault(storeId, string.Empty);
                    byPath[normalizedPath] = new FirefoxProfileInfo(
                        syntheticId,
                        storeProfile.Name,
                        storeProfile.Path,
                        IsRelative: true,
                        installId,
                        storeId);
                }
            }
        }

        return [.. byPath.Values];
    }

    private static IEnumerable<FirefoxStoreProfileInfo> ReadStoreProfiles(string dataPath, string storeId)
    {
        string storePath = Path.Combine(dataPath, "Profile Groups", $"{storeId}.sqlite");
        if (!File.Exists(storePath))
        {
            yield break;
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = storePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT path, name FROM Profiles";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (reader.IsDBNull(0) || reader.IsDBNull(1))
            {
                continue;
            }

            string path = reader.GetString(0);
            string name = reader.GetString(1);
            if (path.Length == 0 || name.Length == 0)
            {
                continue;
            }

            yield return new FirefoxStoreProfileInfo(path, name);
        }
    }

    private static string NormalizeProfilePath(string path) =>
        path.Replace('\\', '/');

    private static string BuildSyntheticProfileId(string storeId, string normalizedPath)
    {
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(normalizedPath));
        return $"store-{storeId}-{Convert.ToHexStringLower(hash)}";
    }

    private sealed record FirefoxStoreProfileInfo(string Path, string Name);
}
