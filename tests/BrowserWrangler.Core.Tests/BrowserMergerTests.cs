using BrowserWrangler.Core.Discovery;
using BrowserWrangler.Core.Models;

namespace BrowserWrangler.Core.Tests;

public class BrowserMergerTests
{
    [Fact]
    public void Merge_preserves_rules_and_user_settings()
    {
        var oldChrome = new Browser("chrome", "Chrome", @"C:\chrome.exe") { IsAutoDiscovered = true, IsHidden = true, SortOrder = 5 };
        var oldProfile = new BrowserProfile(oldChrome, "Default", "Personal") { UserArg = "--dark" };
        oldProfile.Rules.Add(new MatchRule("github.com"));
        oldChrome.Profiles.Add(oldProfile);

        var newChrome = new Browser("chrome", "Chrome", @"C:\chrome.exe") { IsAutoDiscovered = true };
        newChrome.Profiles.Add(new BrowserProfile(newChrome, "Default", "Personal Renamed"));
        newChrome.Profiles.Add(new BrowserProfile(newChrome, "Profile 1", "New Profile"));

        var merged = BrowserMerger.Merge([newChrome], [oldChrome]);

        Assert.Single(merged);
        Assert.True(merged[0].IsHidden);
        Assert.Equal(5, merged[0].SortOrder);
        BrowserProfile def = merged[0].Profiles.Single(p => p.Id == "Default");
        Assert.Single(def.Rules);
        Assert.Equal("--dark", def.UserArg);
        Assert.Empty(merged[0].Profiles.Single(p => p.Id == "Profile 1").Rules);
    }

    [Fact]
    public void Merge_keeps_custom_browsers_and_drops_uninstalled_discovered()
    {
        var custom = new Browser("custom", "My Browser", @"C:\my.exe") { IsAutoDiscovered = false };
        var gone = new Browser("gone", "Uninstalled", @"C:\gone.exe") { IsAutoDiscovered = true };
        var current = new Browser("edge", "Edge", @"C:\msedge.exe") { IsAutoDiscovered = true };

        var merged = BrowserMerger.Merge([current], [custom, gone]);

        Assert.Equal(2, merged.Count);
        Assert.Contains(merged, b => b.Id == "edge");
        Assert.Contains(merged, b => b.Id == "custom");
        Assert.DoesNotContain(merged, b => b.Id == "gone");
    }
}
