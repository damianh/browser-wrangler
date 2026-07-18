using BrowserWrangler.Core.Discovery;
using BrowserWrangler.Core.Models;
using Microsoft.Data.Sqlite;

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
    public void ParseProfilesIni_ignores_profile_groups_section()
    {
        const string ini = """
        [ProfileGroups]
        StoreIds=dedicated-profile

        [Profile0]
        Name=Work
        Path=Profiles/work.default-release
        """;

        FirefoxProfileInfo profile = Assert.Single(FirefoxProfiles.ParseProfilesIni(ini));
        Assert.Equal("Profile0", profile.SectionId);
    }

    [Fact]
    public void ParseProfilesIni_keeps_profiles_with_store_id()
    {
        const string ini = """
        [Profile0]
        Name=Work
        IsRelative=1
        Path=Profiles/work.default-release
        StoreID=dedicated-profile
        """;

        var profiles = FirefoxProfiles.ParseProfilesIni(ini);

        FirefoxProfileInfo profile = Assert.Single(profiles);
        Assert.Equal("Profile0", profile.SectionId);
        Assert.Equal("Work", profile.Name);
        Assert.Equal("Profiles/work.default-release", profile.Path);
        Assert.Equal("dedicated-profile", profile.StoreId);
    }

    [Fact]
    public void Discover_adds_profiles_with_store_id()
    {
        string dir = Path.Combine(Path.GetTempPath(), "bw-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            File.WriteAllText(Path.Combine(dir, "profiles.ini"), """
            [Profile0]
            Name=Work
            IsRelative=1
            Path=Profiles/work.default-release
            StoreID=dedicated-profile
            """);

            var browser = new Browser("firefox", "Firefox", @"C:\Program Files\Mozilla Firefox\firefox.exe")
            {
                IsAutoDiscovered = true,
                Engine = BrowserEngine.Gecko,
                DataPath = dir,
            };

            FirefoxProfiles.Discover(browser);

            Assert.Collection(
                browser.Profiles,
                profile =>
                {
                    Assert.Equal("Profile0", profile.Id);
                    Assert.Equal("Work", profile.Name);
                    Assert.Equal("\"%url%\" -foreground -P \"Work\"", profile.LaunchArg);
                },
                profile =>
                {
                    Assert.Equal("private", profile.Id);
                    Assert.True(profile.IsIncognito);
                });
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void Discover_reads_profile_groups_sqlite_for_modern_profiles()
    {
        string dir = Path.Combine(Path.GetTempPath(), "bw-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

            try
            {
                File.WriteAllText(Path.Combine(dir, "profiles.ini"), """
                [Install308046B0AF4A39CB]
                Default=Profiles/znyagmvf.default-release
                Locked=1

                [Profile0]
                Name=default-release
                IsRelative=1
                Path=Profiles/znyagmvf.default-release
                StoreID=edddb5b7
                """);

                string groupsDir = Path.Combine(dir, "Profile Groups");
                Directory.CreateDirectory(groupsDir);
                string dbPath = Path.Combine(groupsDir, "edddb5b7.sqlite");
                using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = dbPath,
                    Pooling = false,
                }.ToString()))
                {
                    connection.Open();
                    using (SqliteCommand create = connection.CreateCommand())
                    {
                        create.CommandText = """
                        CREATE TABLE Profiles (
                          id INTEGER NOT NULL PRIMARY KEY,
                          path TEXT NOT NULL UNIQUE,
                          name TEXT NOT NULL,
                          avatar TEXT NOT NULL,
                          themeId TEXT NOT NULL,
                          themeFg TEXT NOT NULL,
                          themeBg TEXT NOT NULL
                        );
                        """;
                        create.ExecuteNonQuery();
    }

                    using (SqliteCommand insert = connection.CreateCommand())
                    {
                        insert.CommandText = """
                        INSERT INTO Profiles (id, path, name, avatar, themeId, themeFg, themeBg) VALUES
                        (1, 'Profiles\znyagmvf.default-release', 'Personal', 'craft', 'default-theme@mozilla.org', 'rgb(255,255,255)', 'rgb(28, 27, 34)'),
                        (2, 'Profiles\7KX43bSd.Profile 1', 'Duende', 'briefcase', '{8f6c981d-b7e7-4bd4-9146-ef6b2acf62b4}', 'rgb(255, 255, 255)', 'rgb(98, 57, 151)');
                        """;
                        insert.ExecuteNonQuery();
                    }
                }

                var browser = new Browser("firefox", "Firefox", @"C:\Program Files\Mozilla Firefox\firefox.exe")
                {
                    IsAutoDiscovered = true,
                    Engine = BrowserEngine.Gecko,
                    DataPath = dir,
                };

                FirefoxProfiles.Discover(browser, "308046B0AF4A39CB");

                Assert.Contains(browser.Profiles, p => p.Name == "Personal" && p.Id == "Profile0");
                Assert.Contains(browser.Profiles, p => p.Name == "Duende" && p.LaunchArg == "\"%url%\" -foreground -P \"Duende\"");
                Assert.Contains(browser.Profiles, p => p.Id == "private" && p.IsIncognito);
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
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
