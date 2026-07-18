using BrowserWrangler.Core.Models;
using BrowserWrangler.Core.Pipeline;

namespace BrowserWrangler.Core.Tests;

public class PipelineTests
{
    [Fact]
    public void Substring_replacer_replaces_all_occurrences()
    {
        var step = new ReplacerStep(ReplacerKind.FindReplace, "http://", "https://");
        var payload = new ClickPayload("http://example.com");
        step.Process(payload);
        Assert.Equal("https://example.com", payload.Url);
    }

    [Fact]
    public void Regex_replacer_supports_groups()
    {
        var step = new ReplacerStep(ReplacerKind.Regex, @"twitter\.com", "x.com");
        var payload = new ClickPayload("https://twitter.com/a");
        step.Process(payload);
        Assert.Equal("https://x.com/a", payload.Url);
    }

    [Fact]
    public void Invalid_regex_leaves_url_untouched()
    {
        var step = new ReplacerStep(ReplacerKind.Regex, "[", "x");
        var payload = new ClickPayload("https://example.com");
        step.Process(payload);
        Assert.Equal("https://example.com", payload.Url);
    }

    [Theory]
    [InlineData("substr|find|repl", ReplacerKind.FindReplace, "find", "repl")]
    [InlineData("rgx|f.*|r", ReplacerKind.Regex, "f.*", "r")]
    public void Parse_bt_replacer_format(string line, ReplacerKind kind, string find, string replace)
    {
        var step = ReplacerStep.Parse(line);
        Assert.Equal(kind, step.Kind);
        Assert.Equal(find, step.Find);
        Assert.Equal(replace, step.Replace);
        Assert.Equal(line, step.Serialize());
    }

    [Fact]
    public void Pipeline_runs_steps_in_order()
    {
        var pipeline = new UrlPipeline()
            .Add(new ReplacerStep(ReplacerKind.FindReplace, "a.com", "b.com"))
            .Add(new ReplacerStep(ReplacerKind.FindReplace, "b.com", "c.com"));
        var payload = new ClickPayload("https://a.com");
        pipeline.Process(payload);
        Assert.Equal("https://c.com", payload.Url);
    }
}
