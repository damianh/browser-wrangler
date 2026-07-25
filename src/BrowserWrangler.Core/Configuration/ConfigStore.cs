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

    public AppConfig Load(Version? runningVersion = null)
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

        bool changed = FixUp(config, runningVersion);
        if (changed)
        {
            Save(config);
        }

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
    private static bool FixUp(AppConfig config, Version? runningVersion)
    {
        bool changed = false;
        int clampedIntervalHours = Math.Clamp(config.Updates.CheckIntervalHours, 1, 168);
        if (config.Updates.CheckIntervalHours != clampedIntervalHours)
        {
            config.Updates.CheckIntervalHours = clampedIntervalHours;
            changed = true;
        }

        if (config.Updates.PendingInstallerPath.Length > 0 && !File.Exists(config.Updates.PendingInstallerPath))
        {
            config.Updates.PendingInstallerPath = string.Empty;
            config.Updates.PendingInstallerVersion = string.Empty;
            changed = true;
        }

        if (runningVersion is not null
            && config.Updates.PendingInstallerPath.Length > 0
            && config.Updates.PendingInstallerVersion.Length > 0
            && File.Exists(config.Updates.PendingInstallerPath))
        {
            bool clearPending = !Version.TryParse(config.Updates.PendingInstallerVersion, out Version? pendingVersion)
                || NormalizeVersion(pendingVersion) <= NormalizeVersion(runningVersion);
            if (clearPending)
            {
                try
                {
                    File.Delete(config.Updates.PendingInstallerPath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }

                config.Updates.PendingInstallerPath = string.Empty;
                config.Updates.PendingInstallerVersion = string.Empty;
                changed = true;
            }
        }

        foreach (Browser browser in config.Browsers)
        {
            foreach (BrowserProfile profile in browser.Profiles)
            {
                profile.Browser = browser;
            }
        }

        return changed;
    }

    private static Version NormalizeVersion(Version version) =>
        new(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));
}
