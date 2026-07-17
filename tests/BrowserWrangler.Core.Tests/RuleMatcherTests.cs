using BrowserWrangler.Core.Models;
using BrowserWrangler.Core.Rules;

namespace BrowserWrangler.Core.Tests;

public class RuleMatcherTests
{
    private static (Browser chrome, Browser firefox) MakeBrowsers()
    {
        var chrome = new Browser("chrome", "Chrome", @"C:\chrome.exe") { Engine = BrowserEngine.Chromium };
        chrome.Profiles.Add(new BrowserProfile(chrome, "Default", "Personal"));
        chrome.Profiles.Add(new BrowserProfile(chrome, "Profile 1", "Work"));

        var firefox = new Browser("firefox", "Firefox", @"C:\firefox.exe") { Engine = BrowserEngine.Gecko };
        firefox.Profiles.Add(new BrowserProfile(firefox, "default", "default"));
        return (chrome, firefox);
    }

    [Fact]
    public void Matching_rule_routes_to_profile()
    {
        var (chrome, firefox) = MakeBrowsers();
        chrome.Profiles[1].Rules.Add(new MatchRule("dev.azure.com"));

        var results = RuleMatcher.Match([chrome, firefox], new ClickPayload("https://dev.azure.com/org"));

        Assert.Single(results);
        Assert.Equal("chrome:Profile 1", results[0].Profile.LongId);
        Assert.False(results[0].Rule.IsFallback);
    }

    [Fact]
    public void No_match_returns_fallback_default_profile()
    {
        var (chrome, firefox) = MakeBrowsers();

        var results = RuleMatcher.Match([chrome, firefox], new ClickPayload("https://example.com"), "firefox:default");

        Assert.Single(results);
        Assert.True(results[0].Rule.IsFallback);
        Assert.Equal("firefox:default", results[0].Profile.LongId);
    }

    [Fact]
    public void Unknown_default_falls_back_to_first_profile()
    {
        var (chrome, firefox) = MakeBrowsers();

        var results = RuleMatcher.Match([chrome, firefox], new ClickPayload("https://example.com"), "gone:gone");

        Assert.Equal("chrome:Default", results[0].Profile.LongId);
    }

    [Fact]
    public void Multiple_matches_sorted_by_priority_desc()
    {
        var (chrome, firefox) = MakeBrowsers();
        chrome.Profiles[0].Rules.Add(new MatchRule("github.com") { Priority = 1 });
        firefox.Profiles[0].Rules.Add(new MatchRule("github.com") { Priority = 5 });

        var results = RuleMatcher.Match([chrome, firefox], new ClickPayload("https://github.com"));

        Assert.Equal(2, results.Count);
        Assert.Equal("firefox:default", results[0].Profile.LongId);
    }

    [Fact]
    public void Empty_browser_list_returns_empty()
    {
        Assert.Empty(RuleMatcher.Match([], new ClickPayload("https://example.com")));
    }

    [Fact]
    public void ToProfiles_skips_hidden()
    {
        var (chrome, firefox) = MakeBrowsers();
        chrome.Profiles[1].IsHidden = true;
        firefox.IsHidden = true;

        var profiles = RuleMatcher.ToProfiles([chrome, firefox]);

        Assert.Single(profiles);
        Assert.Equal("chrome:Default", profiles[0].LongId);
    }
}
