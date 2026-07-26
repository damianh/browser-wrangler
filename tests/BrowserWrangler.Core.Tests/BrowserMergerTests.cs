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
        var oldContainer = new BrowserProfile(oldChrome, "Default+c_2", "Personal :: Work");
        oldContainer.Rules.Add(new MatchRule("work.example.com"));
        oldChrome.Profiles.Add(oldContainer);

        var newChrome = new Browser("chrome", "Chrome", @"C:\chrome.exe") { IsAutoDiscovered = true };
        newChrome.Profiles.Add(new BrowserProfile(newChrome, "Default", "Personal Renamed"));
        newChrome.Profiles.Add(new BrowserProfile(newChrome, "Default+c_2", "Personal Renamed :: Work"));
        newChrome.Profiles.Add(new BrowserProfile(newChrome, "Profile 1", "New Profile"));

        var merged = BrowserMerger.Merge([newChrome], [oldChrome]);

        Assert.Single(merged);
        Assert.True(merged[0].IsHidden);
        BrowserProfile def = merged[0].Profiles.Single(p => p.Id == "Default");
        Assert.Single(def.Rules);
        Assert.Equal("--dark", def.UserArg);
        BrowserProfile container = merged[0].Profiles.Single(p => p.Id == "Default+c_2");
        Assert.Single(container.Rules);
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

    [Fact]
    public void Merge_preserves_relative_browser_order_and_normalizes_indices()
    {
        var oldFirefox = new Browser("firefox", "Firefox", @"C:\firefox.exe") { IsAutoDiscovered = true, SortOrder = 3 };
        var oldEdge = new Browser("edge", "Edge", @"C:\msedge.exe") { IsAutoDiscovered = true, SortOrder = 7 };

        var newEdge = new Browser("edge", "Edge", @"C:\msedge.exe") { IsAutoDiscovered = true };
        var newFirefox = new Browser("firefox", "Firefox", @"C:\firefox.exe") { IsAutoDiscovered = true };

        var merged = BrowserMerger.Merge([newEdge, newFirefox], [oldFirefox, oldEdge]);

        Assert.Equal(["firefox", "edge"], merged.Select(b => b.Id));
        Assert.Equal([0, 1], merged.Select(b => b.SortOrder));
    }

    [Fact]
    public void Merge_appends_newly_discovered_browsers_after_existing_ones()
    {
        var oldFirefox = new Browser("firefox", "Firefox", @"C:\firefox.exe") { IsAutoDiscovered = true, SortOrder = 1 };
        var oldEdge = new Browser("edge", "Edge", @"C:\msedge.exe") { IsAutoDiscovered = true, SortOrder = 0 };

        var brandNew = new Browser("zen", "Zen", @"C:\zen.exe") { IsAutoDiscovered = true };
        var newFirefox = new Browser("firefox", "Firefox", @"C:\firefox.exe") { IsAutoDiscovered = true };
        var newEdge = new Browser("edge", "Edge", @"C:\msedge.exe") { IsAutoDiscovered = true };

        var merged = BrowserMerger.Merge([brandNew, newFirefox, newEdge], [oldFirefox, oldEdge]);

        Assert.Equal(["edge", "firefox", "zen"], merged.Select(b => b.Id));
    }

    [Fact]
    public void Merge_profile_order_follows_fresh_discovery_not_saved_order()
    {
        var oldFirefox = new Browser("firefox", "Firefox", @"C:\firefox.exe") { IsAutoDiscovered = true };
        oldFirefox.Profiles.Add(new BrowserProfile(oldFirefox, "private", "Private") { IsIncognito = true, SortOrder = 0 });
        oldFirefox.Profiles.Add(new BrowserProfile(oldFirefox, "Profile0+c_1", "Personal :: Work") { SortOrder = 1 });
        oldFirefox.Profiles.Add(new BrowserProfile(oldFirefox, "Profile0", "Personal") { SortOrder = 2 });

        // fresh discovery: profile, then its containers, incognito last
        var newFirefox = new Browser("firefox", "Firefox", @"C:\firefox.exe") { IsAutoDiscovered = true };
        newFirefox.Profiles.Add(new BrowserProfile(newFirefox, "Profile0", "Personal") { SortOrder = 0 });
        newFirefox.Profiles.Add(new BrowserProfile(newFirefox, "Profile0+c_1", "Personal :: Work") { SortOrder = 1 });
        newFirefox.Profiles.Add(new BrowserProfile(newFirefox, "private", "Private") { IsIncognito = true, SortOrder = 2 });

        var merged = BrowserMerger.Merge([newFirefox], [oldFirefox]);

        Assert.Equal(
            ["Profile0", "Profile0+c_1", "private"],
            merged[0].Profiles.Select(p => p.Id));
        Assert.Equal([0, 1, 2], merged[0].Profiles.Select(p => p.SortOrder));
    }

    [Fact]
    public void Merge_keeps_custom_browser_profile_order()
    {
        var custom = new Browser("custom", "My Browser", @"C:\my.exe") { IsAutoDiscovered = false };
        custom.Profiles.Add(new BrowserProfile(custom, "b", "Second") { SortOrder = 1 });
        custom.Profiles.Add(new BrowserProfile(custom, "a", "First") { SortOrder = 0 });

        var merged = BrowserMerger.Merge([], [custom]);

        Assert.Equal(["a", "b"], merged.Single().Profiles.Select(p => p.Id));
    }
}
