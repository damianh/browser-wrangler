using BrowserWrangler.Core.Discovery;
using BrowserWrangler.Core.Models;

namespace BrowserWrangler.Core.Tests;

public class DiscoveryParserTests
{
    [Fact]
    public void ParseLocalState_reads_profiles_from_info_cache()
    {
        const string json = """
        {
          "profile": {
            "info_cache": {
              "Default": { "name": "Personal", "gaia_picture_file_name": "pic.png" },
              "Profile 1": { "name": "Person 2", "shortcut_name": "Work" }
            }
          }
        }
        """;

        var profiles = ChromiumProfiles.ParseLocalState(json);

        Assert.Equal(2, profiles.Count);
        Assert.Equal(new ChromiumProfileInfo("Default", "Personal", "pic.png"), profiles[0]);
        // shortcut_name wins over name
        Assert.Equal(new ChromiumProfileInfo("Profile 1", "Work", null), profiles[1]);
    }

    [Fact]
    public void ParseLocalState_handles_missing_info_cache()
    {
        Assert.Empty(ChromiumProfiles.ParseLocalState("{}"));
        Assert.Empty(ChromiumProfiles.ParseLocalState("""{"profile":{}}"""));
    }

    [Fact]
    public void ParseProfilesIni_reads_classic_profiles_and_install_binding()
    {
        const string ini = """
        [Install308046B0AF4A39CB]
        Default=Profiles/abc.default-release
        Locked=1

        [Profile1]
        Name=default
        IsRelative=1
        Path=Profiles/xyz.default
        Default=1

        [Profile0]
        Name=default-release
        IsRelative=1
        Path=Profiles/abc.default-release

        [General]
        StartWithLastProfile=1
        Version=2
        """;

        var profiles = FirefoxProfiles.ParseProfilesIni(ini);

        Assert.Equal(2, profiles.Count);
        FirefoxProfileInfo bound = profiles.Single(p => p.SectionId == "Profile0");
        Assert.Equal("default-release", bound.Name);
        Assert.Equal("308046B0AF4A39CB", bound.InstallationId);
        FirefoxProfileInfo unbound = profiles.Single(p => p.SectionId == "Profile1");
        Assert.Equal(string.Empty, unbound.InstallationId);
        Assert.True(unbound.IsRelative);
    }

    [Fact]
    public void ParseProfilesIni_skips_profile_group_stores()
    {
        const string ini = """
        [Profile0]
        Name=group-container
        Path=Profiles/grp
        StoreID=abc123
        """;

        Assert.Empty(FirefoxProfiles.ParseProfilesIni(ini));
    }

    [Fact]
    public void UnmangleOpenCommand_strips_quotes_and_args()
    {
        Assert.Equal(
            @"C:\Program Files\Firefox\firefox.exe",
            BrowserDiscovery.UnmangleOpenCommand("\"C:\\Program Files\\Firefox\\firefox.exe\" -osint -url \"%1\""));
        Assert.Equal(@"C:\chrome.exe", BrowserDiscovery.UnmangleOpenCommand(@"C:\chrome.exe"));
    }

    [Fact]
    public void GetIdFromOpenCommand_is_stable_md5()
    {
        string id1 = BrowserDiscovery.GetIdFromOpenCommand(@"C:\chrome.exe");
        string id2 = BrowserDiscovery.GetIdFromOpenCommand(@"C:\chrome.exe");
        Assert.Equal(id1, id2);
        Assert.Equal(32, id1.Length);
    }

    [Fact]
    public void AddDefaultProfileIfEmpty_adds_single_default()
    {
        var b = new Browser("id", "Some Browser", @"C:\some.exe") { IsAutoDiscovered = true };
        BrowserDiscovery.AddDefaultProfileIfEmpty(b);
        Assert.Single(b.Profiles);
        Assert.Equal("default", b.Profiles[0].Id);
        Assert.Equal("\"%url%\"", b.Profiles[0].LaunchArg);
    }
}
