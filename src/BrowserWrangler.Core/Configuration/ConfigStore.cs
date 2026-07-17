using System.Text.Json;
using System.Text.Json.Serialization;
using BrowserWrangler.Core.Models;

namespace BrowserWrangler.Core.Configuration;

/// <summary>
/// Loads and saves <see cref="AppConfig"/> as JSON. Default location:
/// %LOCALAPPDATA%\BrowserWrangler\config.json.
/// </summary>
public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Converters = { new JsonStringEnumConverter() },
    };

    public ConfigStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BrowserWrangler",
            "config.json"))
    {
    }

    public ConfigStore(string configFilePath) => ConfigFilePath = configFilePath;

    public string ConfigFilePath { get; }

    public AppConfig Load()
    {
        AppConfig config;
        if (!File.Exists(ConfigFilePath))
        {
            config = new AppConfig();
        }
        else
        {
            try
            {
                using FileStream stream = File.OpenRead(ConfigFilePath);
                config = JsonSerializer.Deserialize<AppConfig>(stream, Options) ?? new AppConfig();
            }
            catch (JsonException)
            {
                // corrupt config - start fresh rather than crash the URL-open path
                config = new AppConfig();
            }
        }

        FixUp(config);
        return config;
    }

    public void Save(AppConfig config)
    {
        string? dir = Path.GetDirectoryName(ConfigFilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // write to temp then move, so a crash mid-write can't corrupt the config
        string tmp = ConfigFilePath + ".tmp";
        using (FileStream stream = File.Create(tmp))
        {
            JsonSerializer.Serialize(stream, config, Options);
        }

        File.Move(tmp, ConfigFilePath, overwrite: true);
    }

    /// <summary>Restores non-serialized back references after deserialization.</summary>
    private static void FixUp(AppConfig config)
    {
        foreach (Browser browser in config.Browsers)
        {
            foreach (BrowserProfile profile in browser.Profiles)
            {
                profile.Browser = browser;
            }
        }
    }
}
