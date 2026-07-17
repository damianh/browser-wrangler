using System.Text.Json;
using BrowserWrangler.Core.Models;

namespace BrowserWrangler.Core.Discovery;

/// <summary>A profile parsed from a Chromium "Local State" file.</summary>
public sealed record ChromiumProfileInfo(string SystemName, string DisplayName, string? GaiaPictureFileName);

/// <summary>
/// Discovers Chromium browser profiles from the "Local State" JSON in the browser's data folder.
/// </summary>
public static class ChromiumProfiles
{
    /// <summary>Extra args added to all Chromium launches, mirroring bt.</summary>
    public const string ExtraArgs = " --no-default-browser-check";

    /// <summary>Parses profile info from Local State JSON content ($.profile.info_cache).</summary>
    public static List<ChromiumProfileInfo> ParseLocalState(string json)
    {
        var result = new List<ChromiumProfileInfo>();
        using JsonDocument doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("profile", out JsonElement profile) ||
            !profile.TryGetProperty("info_cache", out JsonElement infoCache) ||
            infoCache.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (JsonProperty entry in infoCache.EnumerateObject())
        {
            string sysName = entry.Name;
            string name = string.Empty;
            if (entry.Value.TryGetProperty("shortcut_name", out JsonElement shortcut) &&
                shortcut.ValueKind == JsonValueKind.String)
            {
                name = shortcut.GetString()!;
            }

            if (name.Length == 0 &&
                entry.Value.TryGetProperty("name", out JsonElement n) &&
                n.ValueKind == JsonValueKind.String)
            {
                name = n.GetString()!;
            }

            string? picture = null;
            if (entry.Value.TryGetProperty("gaia_picture_file_name", out JsonElement pic) &&
                pic.ValueKind == JsonValueKind.String)
            {
                picture = pic.GetString();
            }

            result.Add(new ChromiumProfileInfo(sysName, name, picture));
        }

        return result;
    }

    /// <summary>Populates <paramref name="browser"/>.Profiles from its data folder.</summary>
    public static void Discover(Browser browser)
    {
        if (!browser.IsAutoDiscovered || browser.Engine != BrowserEngine.Chromium)
        {
            return;
        }

        string localStatePath = Path.Combine(browser.DataPath, "Local State");
        if (File.Exists(localStatePath))
        {
            List<ChromiumProfileInfo> infos;
            try
            {
                infos = ParseLocalState(File.ReadAllText(localStatePath));
            }
            catch (JsonException)
            {
                infos = [];
            }

            foreach (ChromiumProfileInfo info in infos)
            {
                string arg = $"\"{BrowserProfile.UrlArgName}\" \"--profile-directory={info.SystemName}\"{ExtraArgs}";
                var profile = new BrowserProfile(browser, info.SystemName, info.DisplayName, arg)
                {
                    SortOrder = browser.Profiles.Count,
                };
                if (info.GaiaPictureFileName is { Length: > 0 })
                {
                    profile.IconPath = Path.Combine(browser.DataPath, info.SystemName, info.GaiaPictureFileName);
                }

                browser.Profiles.Add(profile);
            }
        }

        // private-mode instance (Edge names it differently)
        bool isEdge = browser.OpenCommand.Contains("msedge.exe", StringComparison.OrdinalIgnoreCase);
        string privName = isEdge ? "InPrivate" : "Incognito";
        string privArg = isEdge
            ? $"\"{BrowserProfile.UrlArgName}\" --inprivate"
            : $"\"{BrowserProfile.UrlArgName}\" --incognito";
        browser.Profiles.Add(new BrowserProfile(browser, privName, privName, privArg)
        {
            IsIncognito = true,
            SortOrder = browser.Profiles.Count,
        });
    }
}
