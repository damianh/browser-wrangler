using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

/// <summary>A Firefox Multi-Account Container parsed from containers.json.</summary>
public sealed record FirefoxContainerInfo(string Id, string Name);

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

            string argSuffix = $" -foreground -P \"{info.Name}\"";
            string arg = $"\"{BrowserProfile.UrlArgName}\"{argSuffix}";
            browser.Profiles.Add(new BrowserProfile(browser, info.SectionId, info.Name, arg)
            {
                SortOrder = browser.Profiles.Count,
            });

            foreach (FirefoxContainerInfo container in ReadContainersForProfile(browser.DataPath, info))
            {
                string encodedName = Uri.EscapeDataString(container.Name);
                string containerArg =
                    $"\"ext+container:name={encodedName}&url={BrowserProfile.UrlEncodedArgName}\"{argSuffix}";
                browser.Profiles.Add(new BrowserProfile(
                    browser,
                    $"{info.SectionId}+c_{container.Id}",
                    $"{info.Name} :: {container.Name}",
                    containerArg)
                {
                    SortOrder = browser.Profiles.Count,
                });
            }
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

    /// <summary>Parses Firefox containers from a profile's containers.json content.</summary>
    public static List<FirefoxContainerInfo> ParseContainersJson(string jsonContent)
    {
        var result = new List<FirefoxContainerInfo>();

        try
        {
            using JsonDocument document = JsonDocument.Parse(jsonContent);
            if (!document.RootElement.TryGetProperty("identities", out JsonElement identities) ||
                identities.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (JsonElement identity in identities.EnumerateArray())
            {
                if (!identity.TryGetProperty("public", out JsonElement isPublic) ||
                    isPublic.ValueKind != JsonValueKind.True)
                {
                    continue;
                }

                if (!identity.TryGetProperty("userContextId", out JsonElement userContextId) ||
                    !userContextId.TryGetInt32(out int id))
                {
                    continue;
                }

                string? name = identity.TryGetProperty("name", out JsonElement explicitName) &&
                               explicitName.ValueKind == JsonValueKind.String
                    ? explicitName.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(name))
                {
                    string? localizationId = TryGetString(identity, "l10nID") ?? TryGetString(identity, "l10nId");
                    if (localizationId is null)
                    {
                        continue;
                    }

                    name = ResolveDefaultContainerName(localizationId);
                }

                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                result.Add(new FirefoxContainerInfo(id.ToString(), name));
            }
        }
        catch (JsonException)
        {
            return [];
        }

        return result;
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

    private static IEnumerable<FirefoxContainerInfo> ReadContainersForProfile(string dataPath, FirefoxProfileInfo profile)
    {
        string profilePath = ResolveProfilePath(dataPath, profile);
        if (!Directory.Exists(profilePath))
        {
            yield break;
        }

        string containersPath = Path.Combine(profilePath, "containers.json");
        if (!File.Exists(containersPath))
        {
            yield break;
        }

        string containersJson;
        try
        {
            containersJson = File.ReadAllText(containersPath);
        }
        catch (IOException)
        {
            yield break;
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (FirefoxContainerInfo container in ParseContainersJson(containersJson))
        {
            yield return container;
        }
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

    private static string ResolveProfilePath(string dataPath, FirefoxProfileInfo profile) =>
        profile.IsRelative
            ? Path.GetFullPath(Path.Combine(dataPath, profile.Path.Replace('/', Path.DirectorySeparatorChar)))
            : profile.Path;

    private static string? TryGetString(JsonElement element, string key) =>
        element.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string ResolveDefaultContainerName(string localizationId) => localizationId switch
    {
        "userContextPersonal.label" or "user-context-personal" => "Personal",
        "userContextWork.label" or "user-context-work" => "Work",
        "userContextBanking.label" or "user-context-banking" => "Banking",
        "userContextShopping.label" or "user-context-shopping" => "Shopping",
        _ => localizationId,
    };

    private sealed record FirefoxStoreProfileInfo(string Path, string Name);
}
