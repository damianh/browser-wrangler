using BrowserWrangler.Core.Models;

namespace BrowserWrangler.Core.Tests;

public class MatchRuleTests
{
    [Theory]
    [InlineData("github.com", "https://github.com/damianh", true)]
    [InlineData("GITHUB.com", "https://github.com/damianh", true)] // case-insensitive
    [InlineData("gitlab.com", "https://github.com/damianh", false)]
    [InlineData("damianh", "https://github.com/damianh", true)]
    public void Substring_matches_anywhere_in_url(string value, string url, bool expected)
    {
        var rule = new MatchRule(value);
        Assert.Equal(expected, rule.IsMatch(url));
    }

    [Fact]
    public void Domain_scope_only_matches_host()
    {
        var rule = new MatchRule("github.com") { Scope = MatchScope.Domain };
        Assert.True(rule.IsMatch("https://github.com/foo"));
        Assert.False(rule.IsMatch("https://example.com/github.com"));
    }

    [Fact]
    public void Path_scope_only_matches_path()
    {
        var rule = new MatchRule("issues") { Scope = MatchScope.Path };
        Assert.True(rule.IsMatch("https://github.com/damianh/bt/issues/1"));
        Assert.False(rule.IsMatch("https://issues.example.com/"));
    }

    [Fact]
    public void Regex_requires_full_match()
    {
        var rule = new MatchRule(@"https://github\.com/.*") { IsRegex = true };
        Assert.True(rule.IsMatch("https://github.com/damianh"));
        // partial regex match is not enough (mirrors std::regex_match)
        var partial = new MatchRule("github") { IsRegex = true };
        Assert.False(partial.IsMatch("https://github.com"));
    }

    [Fact]
    public void Invalid_regex_does_not_throw_and_never_matches()
    {
        var rule = new MatchRule("[") { IsRegex = true };
        Assert.False(rule.IsMatch("https://github.com"));
    }

    [Fact]
    public void Empty_value_never_matches()
    {
        Assert.False(new MatchRule("").IsMatch("https://github.com"));
    }

    [Fact]
    public void Window_title_and_process_name_locations()
    {
        var title = new MatchRule("Outlook") { Location = MatchLocation.WindowTitle };
        var proc = new MatchRule("slack") { Location = MatchLocation.ProcessName };
        var payload = new ClickPayload("https://example.com")
        {
            WindowTitle = "Inbox - Outlook",
            ProcessName = "slack.exe",
        };
        Assert.True(title.IsMatch(payload));
        Assert.True(proc.IsMatch(payload));
        Assert.False(title.IsMatch(new ClickPayload("https://example.com")));
    }

    [Theory]
    [InlineData("https://github.com/a/b", "https", "github.com", "a/b")]
    [InlineData("github.com/a", "", "github.com", "a")]
    [InlineData("github.com", "", "github.com", "")]
    [InlineData("https://github.com", "https", "github.com", "")]
    public void ParseUrl_splits_proto_host_path(string url, string proto, string host, string path)
    {
        MatchRule.ParseUrl(url, out string pr, out string h, out string p);
        Assert.Equal(proto, pr);
        Assert.Equal(host, h);
        Assert.Equal(path, p);
    }

    [Fact]
    public void Parse_bt_line_format_roundtrips()
    {
        var rule = MatchRule.Parse("loc:window_title|scope:domain|priority:3|mode:app|type:regex|my value");
        Assert.Equal(MatchLocation.WindowTitle, rule.Location);
        Assert.Equal(MatchScope.Domain, rule.Scope);
        Assert.Equal(3, rule.Priority);
        Assert.True(rule.AppMode);
        Assert.True(rule.IsRegex);
        Assert.Equal("my value", rule.Value);
        Assert.Equal("loc:window_title|scope:domain|priority:3|mode:app|type:regex|my value", rule.ToLine());
    }

    [Fact]
    public void Parse_plain_value_uses_defaults()
    {
        var rule = MatchRule.Parse("github.com");
        Assert.Equal("github.com", rule.Value);
        Assert.Equal(MatchLocation.Url, rule.Location);
        Assert.Equal(MatchScope.Any, rule.Scope);
        Assert.Equal(0, rule.Priority);
        Assert.False(rule.IsRegex);
        Assert.Equal("github.com", rule.ToLine());
    }

    [Fact]
    public void Rules_with_same_value_and_scope_are_equal()
    {
        Assert.Equal(new MatchRule("a"), new MatchRule("a"));
        Assert.NotEqual(new MatchRule("a"), new MatchRule("a") { Scope = MatchScope.Domain });
    }
}
