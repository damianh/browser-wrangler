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
        config.Pipeline.UnwrapSafelinks = false;
        config.Pipeline.ExpandShortenedUrls = true;
        config.Pipeline.Substitutions.Add("substr|http://|https://");
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
        Assert.False(loaded.Pipeline.UnwrapSafelinks);
        Assert.True(loaded.Pipeline.ExpandShortenedUrls);
        Assert.Equal("substr|http://|https://", loaded.Pipeline.Substitutions[0]);
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
}
