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
    private static readonly string DefaultManagedUpdatesDirectoryPath = Path.GetFullPath(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BrowserWrangler",
        "updates"));

    public ConfigStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BrowserWrangler",
            "config.json"), DefaultManagedUpdatesDirectoryPath)
    {
    }

    public ConfigStore(string configFilePath, string? managedUpdatesDirectoryPath = null)
    {
        ConfigFilePath = configFilePath;
        ManagedUpdatesDirectoryPath = Path.GetFullPath(managedUpdatesDirectoryPath ?? DefaultManagedUpdatesDirectoryPath);
    }

    public string ConfigFilePath { get; }
    private string ManagedUpdatesDirectoryPath { get; }

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

        _ = FixUp(config, runningVersion);
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
    private bool FixUp(AppConfig config, Version? runningVersion)
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

        if (config.Updates.PendingInstallerPath.Length > 0
            && !TryNormalizeManagedPendingInstallerPathForStore(config.Updates.PendingInstallerPath, out _))
        {
            config.Updates.PendingInstallerPath = string.Empty;
            config.Updates.PendingInstallerVersion = string.Empty;
            changed = true;
        }

        if (runningVersion is not null
            && config.Updates.PendingInstallerPath.Length > 0
            && File.Exists(config.Updates.PendingInstallerPath))
        {
            bool clearPending = !Version.TryParse(config.Updates.PendingInstallerVersion, out Version? pendingVersion)
                || NormalizeVersion(pendingVersion) <= NormalizeVersion(runningVersion);
            if (clearPending)
            {
                if (TryNormalizeManagedPendingInstallerPathForStore(config.Updates.PendingInstallerPath, out string managedPendingInstallerPath))
                {
                    try
                    {
                        File.Delete(managedPendingInstallerPath);
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
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

    public static bool TryNormalizeManagedPendingInstallerPath(string pendingInstallerPath, out string normalizedManagedPath) =>
        TryNormalizeManagedPendingInstallerPath(pendingInstallerPath, DefaultManagedUpdatesDirectoryPath, out normalizedManagedPath);

    private bool TryNormalizeManagedPendingInstallerPathForStore(string pendingInstallerPath, out string normalizedManagedPath) =>
        TryNormalizeManagedPendingInstallerPath(pendingInstallerPath, ManagedUpdatesDirectoryPath, out normalizedManagedPath);

    private static bool TryNormalizeManagedPendingInstallerPath(string pendingInstallerPath, string managedUpdatesDirectoryPath, out string normalizedManagedPath)
    {
        normalizedManagedPath = string.Empty;
        if (pendingInstallerPath.Length == 0)
        {
            return false;
        }

        try
        {
            string normalizedPath = Path.GetFullPath(pendingInstallerPath);
            string managedRoot = managedUpdatesDirectoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!normalizedPath.StartsWith(managedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            normalizedManagedPath = normalizedPath;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
