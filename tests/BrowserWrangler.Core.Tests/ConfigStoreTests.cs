using BrowserWrangler.Core.Configuration;
using BrowserWrangler.Core.Models;

namespace BrowserWrangler.Core.Tests;

public class ConfigStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bw-tests-" + Guid.NewGuid().ToString("N"));

    private ConfigStore MakeStore() => new(Path.Combine(_dir, "config.json"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Load_returns_defaults_when_file_missing()
    {
        AppConfig config = MakeStore().Load();
        Assert.True(config.Toast.ShowOnOpen);
        Assert.True(config.Picker.OnCtrlShift);
        Assert.True(config.Pipeline.UnwrapSafelinks);
        Assert.False(config.Pipeline.ExpandShortenedUrls);
        Assert.False(config.HandleHtmlFiles);
        Assert.True(config.Updates.AutoCheckEnabled);
        Assert.Equal(24, config.Updates.CheckIntervalHours);
        Assert.Empty(config.Browsers);
    }

    [Fact]
    public void Save_and_load_roundtrips_browsers_rules_and_settings()
    {
        ConfigStore store = MakeStore();
        var chrome = new Browser("chrome", "Chrome", @"C:\chrome.exe") { Engine = BrowserEngine.Chromium };
        var profile = new BrowserProfile(chrome, "Default", "Personal", "--profile-directory=\"Default\"");
        profile.Rules.Add(new MatchRule("github.com") { Scope = MatchScope.Domain, Priority = 2 });
        chrome.Profiles.Add(profile);

        var config = new AppConfig
        {
            DefaultProfile = "chrome:Default",
            Theme = "dark",
            Browsers = [chrome],
        };
        config.Picker.OnCtrlShift = false;
        config.Picker.OnConflict = false;
        config.Picker.ShowKeyHints = false;
        config.Toast.ShowOnOpen = false;
        config.Pipeline.UnwrapSafelinks = false;
        config.Pipeline.ExpandShortenedUrls = true;
        config.Pipeline.Substitute = false;
        config.Pipeline.Substitutions.Add("substr|http://|https://");
        config.HandleHtmlFiles = true;
        config.Updates.AutoCheckEnabled = false;
        config.Updates.CheckIntervalHours = 48;
        config.Updates.AutoDownloadInstaller = true;
        store.Save(config);

        AppConfig loaded = store.Load();

        Assert.Equal("dark", loaded.Theme);
        Assert.Equal("chrome:Default", loaded.DefaultProfile);
        Assert.Single(loaded.Browsers);
        BrowserProfile p = loaded.Browsers[0].Profiles[0];
        Assert.Same(loaded.Browsers[0], p.Browser); // back-reference fixed up
        Assert.Equal("chrome:Default", p.LongId);
        Assert.Single(p.Rules);
        Assert.Equal(MatchScope.Domain, p.Rules[0].Scope);
        Assert.Equal(2, p.Rules[0].Priority);
        Assert.False(loaded.Picker.OnCtrlShift);
        Assert.False(loaded.Picker.OnConflict);
        Assert.False(loaded.Picker.ShowKeyHints);
        Assert.False(loaded.Toast.ShowOnOpen);
        Assert.False(loaded.Pipeline.UnwrapSafelinks);
        Assert.True(loaded.Pipeline.ExpandShortenedUrls);
        Assert.False(loaded.Pipeline.Substitute);
        Assert.Equal("substr|http://|https://", loaded.Pipeline.Substitutions[0]);
        Assert.True(loaded.HandleHtmlFiles);
        Assert.False(loaded.Updates.AutoCheckEnabled);
        Assert.Equal(48, loaded.Updates.CheckIntervalHours);
        Assert.True(loaded.Updates.AutoDownloadInstaller);
    }

    [Fact]
    public void Corrupt_config_falls_back_to_defaults()
    {
        ConfigStore store = MakeStore();
        Directory.CreateDirectory(_dir);
        File.WriteAllText(store.ConfigFilePath, "{ not json !!");

        AppConfig config = store.Load();

        Assert.Empty(config.Browsers);
    }

    [Fact]
    public void Load_clamps_update_interval_and_clears_missing_pending_installer()
    {
        ConfigStore store = MakeStore();
        Directory.CreateDirectory(_dir);
        File.WriteAllText(
            store.ConfigFilePath,
            """
            {
              "Updates": {
                "CheckIntervalHours": 1000,
                "PendingInstallerPath": "C:\\missing\\installer.exe",
                "PendingInstallerVersion": "2026.801.4"
              }
            }
            """);

        AppConfig config = store.Load();

        Assert.Equal(168, config.Updates.CheckIntervalHours);
        Assert.Equal(string.Empty, config.Updates.PendingInstallerPath);
        Assert.Equal(string.Empty, config.Updates.PendingInstallerVersion);
    }

    [Fact]
    public void Load_recovers_when_updates_or_update_strings_are_null()
    {
        ConfigStore store = MakeStore();
        Directory.CreateDirectory(_dir);
        File.WriteAllText(
            store.ConfigFilePath,
            """
            {
              "Updates": null
            }
            """);

        AppConfig config = store.Load();

        Assert.NotNull(config.Updates);
        Assert.Equal(24, config.Updates.CheckIntervalHours);
        Assert.Equal(string.Empty, config.Updates.PendingInstallerPath);
        Assert.Equal(string.Empty, config.Updates.PendingInstallerVersion);

        File.WriteAllText(
            store.ConfigFilePath,
            """
            {
              "Updates": {
                "LastCheckUtc": null,
                "PendingInstallerPath": null,
                "PendingInstallerVersion": null
              }
            }
            """);

        config = store.Load();

        Assert.Equal(string.Empty, config.Updates.LastCheckUtc);
        Assert.Equal(string.Empty, config.Updates.PendingInstallerPath);
        Assert.Equal(string.Empty, config.Updates.PendingInstallerVersion);
    }

    [Fact]
    public void Setup_is_incomplete_until_the_first_run_window_marks_it_done()
    {
        ConfigStore store = MakeStore();
        Assert.False(store.Load().SetupCompleted);

        store.Save(new AppConfig { SetupCompleted = true });

        Assert.True(store.Load().SetupCompleted);
    }

    [Fact]
    public void Load_clears_and_deletes_pending_installer_when_not_newer_than_running_app()
    {
        string updatesDir = Path.Combine(_dir, "updates");
        ConfigStore store = new(Path.Combine(_dir, "config.json"), updatesDir);
        Directory.CreateDirectory(_dir);
        Directory.CreateDirectory(updatesDir);
        string pendingInstallerPath = Path.Combine(updatesDir, $"BrowserWrangler-{Guid.NewGuid():N}-setup.exe");
        File.WriteAllText(pendingInstallerPath, "installer");
        File.WriteAllText(
            store.ConfigFilePath,
            $$"""
            {
              "Updates": {
                "PendingInstallerPath": "{{pendingInstallerPath.Replace("\\", "\\\\")}}",
                "PendingInstallerVersion": "2026.801.4"
              }
            }
            """);

        AppConfig config = store.Load(new Version(2026, 801, 4));

        Assert.Equal(string.Empty, config.Updates.PendingInstallerPath);
        Assert.Equal(string.Empty, config.Updates.PendingInstallerVersion);
        Assert.False(File.Exists(pendingInstallerPath));
    }

    [Fact]
    public void Load_clears_stale_pending_installer_metadata_without_deleting_unmanaged_path()
    {
        ConfigStore store = MakeStore();
        Directory.CreateDirectory(_dir);
        string pendingInstallerPath = Path.Combine(_dir, "BrowserWrangler-2026.801.4-x64-setup.exe");
        File.WriteAllText(pendingInstallerPath, "installer");
        File.WriteAllText(
            store.ConfigFilePath,
            $$"""
            {
              "Updates": {
                "PendingInstallerPath": "{{pendingInstallerPath.Replace("\\", "\\\\")}}",
                "PendingInstallerVersion": "2026.801.4"
              }
            }
            """);

        AppConfig config = store.Load(new Version(2026, 801, 4));

        Assert.Equal(string.Empty, config.Updates.PendingInstallerPath);
        Assert.Equal(string.Empty, config.Updates.PendingInstallerVersion);
        Assert.True(File.Exists(pendingInstallerPath));
    }

    [Fact]
    public void Load_clears_pending_installer_with_empty_version_when_running_version_is_known()
    {
        string updatesDir = Path.Combine(_dir, "updates");
        ConfigStore store = new(Path.Combine(_dir, "config.json"), updatesDir);
        Directory.CreateDirectory(_dir);
        Directory.CreateDirectory(updatesDir);
        string pendingInstallerPath = Path.Combine(updatesDir, $"BrowserWrangler-{Guid.NewGuid():N}-setup.exe");
        File.WriteAllText(pendingInstallerPath, "installer");
        File.WriteAllText(
            store.ConfigFilePath,
            $$"""
            {
              "Updates": {
                "PendingInstallerPath": "{{pendingInstallerPath.Replace("\\", "\\\\")}}",
                "PendingInstallerVersion": ""
              }
            }
            """);

        AppConfig config = store.Load(new Version(2026, 801, 4));

        Assert.Equal(string.Empty, config.Updates.PendingInstallerPath);
        Assert.Equal(string.Empty, config.Updates.PendingInstallerVersion);
        Assert.False(File.Exists(pendingInstallerPath));
    }

    [Fact]
    public void Load_clears_newer_pending_installer_metadata_without_deleting_unmanaged_path()
    {
        ConfigStore store = MakeStore();
        Directory.CreateDirectory(_dir);
        string pendingInstallerPath = Path.Combine(_dir, "BrowserWrangler-2026.801.4-x64-setup.exe");
        File.WriteAllText(pendingInstallerPath, "installer");
        File.WriteAllText(
            store.ConfigFilePath,
            $$"""
            {
              "Updates": {
                "PendingInstallerPath": "{{pendingInstallerPath.Replace("\\", "\\\\")}}",
                "PendingInstallerVersion": "2026.801.5"
              }
            }
            """);

        AppConfig config = store.Load(new Version(2026, 801, 4));

        Assert.Equal(string.Empty, config.Updates.PendingInstallerPath);
        Assert.Equal(string.Empty, config.Updates.PendingInstallerVersion);
        Assert.True(File.Exists(pendingInstallerPath));
    }

    [Fact]
    public void Load_keeps_pending_installer_when_it_is_newer_than_running_app_and_managed()
    {
        string updatesDir = Path.Combine(_dir, "updates");
        ConfigStore store = new(Path.Combine(_dir, "config.json"), updatesDir);
        Directory.CreateDirectory(_dir);
        Directory.CreateDirectory(updatesDir);
        string pendingInstallerPath = Path.Combine(updatesDir, $"BrowserWrangler-{Guid.NewGuid():N}-setup.exe");
        File.WriteAllText(pendingInstallerPath, "installer");
        File.WriteAllText(
            store.ConfigFilePath,
            $$"""
            {
              "Updates": {
                "PendingInstallerPath": "{{pendingInstallerPath.Replace("\\", "\\\\")}}",
                "PendingInstallerVersion": "2026.801.5"
              }
            }
            """);

        AppConfig config = store.Load(new Version(2026, 801, 4));

        Assert.Equal(pendingInstallerPath, config.Updates.PendingInstallerPath);
        Assert.Equal("2026.801.5", config.Updates.PendingInstallerVersion);
        Assert.True(File.Exists(pendingInstallerPath));
    }

    [Fact]
    public void Load_normalizes_and_keeps_managed_pending_installer_path()
    {
        string updatesDir = Path.Combine(_dir, "updates");
        ConfigStore store = new(Path.Combine(_dir, "config.json"), updatesDir);
        Directory.CreateDirectory(_dir);
        Directory.CreateDirectory(updatesDir);
        string pendingInstallerPath = Path.Combine(updatesDir, $"BrowserWrangler-{Guid.NewGuid():N}-setup.exe");
        File.WriteAllText(pendingInstallerPath, "installer");
        string nonNormalizedPath = Path.Combine(updatesDir, ".", "subdir", "..", Path.GetFileName(pendingInstallerPath));

        File.WriteAllText(
            store.ConfigFilePath,
            $$"""
            {
              "Updates": {
                "PendingInstallerPath": "{{nonNormalizedPath.Replace("\\", "\\\\")}}",
                "PendingInstallerVersion": "2026.801.5"
              }
            }
            """);

        AppConfig config = store.Load(new Version(2026, 801, 4));

        Assert.Equal(pendingInstallerPath, config.Updates.PendingInstallerPath);
        Assert.Equal("2026.801.5", config.Updates.PendingInstallerVersion);
    }
}
