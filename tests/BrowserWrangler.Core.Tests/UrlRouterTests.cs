using BrowserWrangler.Core.Configuration;
using BrowserWrangler.Core.Launching;
using BrowserWrangler.Core.Models;

namespace BrowserWrangler.Core.Tests;

public class UrlRouterTests
{
    private static AppConfig MakeConfig()
    {
        var chrome = new Browser("chrome", "Chrome", @"C:\chrome.exe") { Engine = BrowserEngine.Chromium };
        chrome.Profiles.Add(new BrowserProfile(chrome, "Default", "Personal", $"\"{BrowserProfile.UrlArgName}\" --profile-directory=Default"));
        chrome.Profiles.Add(new BrowserProfile(chrome, "Work", "Work"));
        var config = new AppConfig { Browsers = [chrome], DefaultProfile = "chrome:Default" };
        config.Picker.OnConflict = true;
        config.Picker.OnNoRule = false;
        config.Picker.Always = false;
        return config;
    }

    [Fact]
    public void Single_rule_match_opens_directly()
    {
        AppConfig config = MakeConfig();
        config.Browsers[0].Profiles[1].Rules.Add(new MatchRule("github.com"));

        RouteDecision d = UrlRouter.Route(config, new ClickPayload("https://github.com"));

        Assert.Equal(RouteAction.Open, d.Action);
        Assert.Equal("chrome:Work", d.Matches[0].Profile.LongId);
    }

    [Fact]
    public void No_rule_opens_default_when_onNoRule_off()
    {
        AppConfig config = MakeConfig();

        RouteDecision d = UrlRouter.Route(config, new ClickPayload("https://example.com"));

        Assert.Equal(RouteAction.Open, d.Action);
        Assert.Equal("chrome:Default", d.Matches[0].Profile.LongId);
        Assert.True(d.Matches[0].Rule.IsFallback);
    }

    [Fact]
    public void No_rule_shows_picker_when_onNoRule_on()
    {
        AppConfig config = MakeConfig();
        config.Picker.OnNoRule = true;

        RouteDecision d = UrlRouter.Route(config, new ClickPayload("https://example.com"));

        Assert.Equal(RouteAction.ShowPicker, d.Action);
    }

    [Fact]
    public void Conflicting_rules_show_picker()
    {
        AppConfig config = MakeConfig();
        config.Browsers[0].Profiles[0].Rules.Add(new MatchRule("github.com"));
        config.Browsers[0].Profiles[1].Rules.Add(new MatchRule("github.com"));

        RouteDecision d = UrlRouter.Route(config, new ClickPayload("https://github.com"));

        Assert.Equal(RouteAction.ShowPicker, d.Action);
        Assert.Equal(2, d.Matches.Count);
    }

    [Fact]
    public void Picker_hotkey_overrides_direct_open()
    {
        AppConfig config = MakeConfig();
        config.Browsers[0].Profiles[1].Rules.Add(new MatchRule("github.com"));

        RouteDecision d = UrlRouter.Route(config, new ClickPayload("https://github.com"), pickerRequested: true);

        Assert.Equal(RouteAction.ShowPicker, d.Action);
    }

    [Fact]
    public void Pipeline_substitutions_run_before_matching()
    {
        AppConfig config = MakeConfig();
        config.Pipeline.Substitutions.Add("substr|twitter.com|x.com");
        config.Browsers[0].Profiles[1].Rules.Add(new MatchRule("x.com"));

        RouteDecision d = UrlRouter.Route(config, new ClickPayload("https://twitter.com/a"));

        Assert.Equal("https://x.com/a", d.Payload.Url);
        Assert.Equal(RouteAction.Open, d.Action);
        Assert.Equal("chrome:Work", d.Matches[0].Profile.LongId);
    }

    [Fact]
    public void Safelinks_are_unwrapped_before_matching()
    {
        AppConfig config = MakeConfig();
        config.Browsers[0].Profiles[1].Rules.Add(new MatchRule("github.com"));

        RouteDecision d = UrlRouter.Route(
            config,
            new ClickPayload("https://nam01.safelinks.protection.outlook.com/?url=https%3A%2F%2Fgithub.com%2Fdamianh%2Fbrowser-wrangler&data=123"));

        Assert.Equal("https://github.com/damianh/browser-wrangler", d.Payload.Url);
        Assert.Equal(RouteAction.Open, d.Action);
        Assert.Equal("chrome:Work", d.Matches[0].Profile.LongId);
    }

    [Fact]
    public void No_browsers_returns_none()
    {
        var config = new AppConfig();
        RouteDecision d = UrlRouter.Route(config, new ClickPayload("https://example.com"));
        Assert.Equal(RouteAction.None, d.Action);
    }

    [Fact]
    public void App_mode_rule_applied_to_payload_on_open()
    {
        AppConfig config = MakeConfig();
        config.Browsers[0].Profiles[1].Rules.Add(new MatchRule("app.example.com") { AppMode = true });

        RouteDecision d = UrlRouter.Route(config, new ClickPayload("https://app.example.com"));

        Assert.Equal(RouteAction.Open, d.Action);
        Assert.True(d.Payload.AppMode);
    }
}

public class BrowserLauncherTests
{
    [Fact]
    public void BuildArguments_substitutes_url_placeholder()
    {
        var b = new Browser("c", "Chrome", @"C:\chrome.exe") { Engine = BrowserEngine.Chromium };
        var p = new BrowserProfile(b, "Default", "Personal", $"\"{BrowserProfile.UrlArgName}\" \"--profile-directory=Default\"");

        string args = BrowserLauncher.BuildArguments(p, new ClickPayload("https://example.com"));

        Assert.Equal("\"https://example.com\" \"--profile-directory=Default\"", args);
    }

    [Fact]
    public void BuildArguments_empty_launch_arg_uses_url()
    {
        var b = new Browser("x", "X", @"C:\x.exe");
        var p = new BrowserProfile(b, "default", "Default");

        Assert.Equal("https://example.com", BrowserLauncher.BuildArguments(p, new ClickPayload("https://example.com")));
    }

    [Fact]
    public void BuildArguments_app_mode_only_for_chromium()
    {
        var chromium = new Browser("c", "Chrome", @"C:\chrome.exe") { Engine = BrowserEngine.Chromium };
        var gecko = new Browser("f", "Firefox", @"C:\firefox.exe") { Engine = BrowserEngine.Gecko };
        var payload = new ClickPayload("https://example.com") { AppMode = true };

        string cArgs = BrowserLauncher.BuildArguments(new BrowserProfile(chromium, "d", "D"), payload);
        string fArgs = BrowserLauncher.BuildArguments(new BrowserProfile(gecko, "d", "D"), payload);

        Assert.StartsWith("--app=", cArgs);
        Assert.DoesNotContain("--app=", fArgs);
    }

    [Fact]
    public void BuildArguments_appends_user_arg()
    {
        var b = new Browser("c", "Chrome", @"C:\chrome.exe");
        var p = new BrowserProfile(b, "d", "D") { UserArg = "--force-dark-mode" };

        Assert.EndsWith(" --force-dark-mode", BrowserLauncher.BuildArguments(p, new ClickPayload("https://e.com")));
    }
}
