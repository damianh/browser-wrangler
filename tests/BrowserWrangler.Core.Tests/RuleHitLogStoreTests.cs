using BrowserWrangler.Core.Logging;

namespace BrowserWrangler.Core.Tests;

public class RuleHitLogStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bw-rule-hit-tests-" + Guid.NewGuid().ToString("N"));

    private RuleHitLogStore MakeStore() => new(Path.Combine(_dir, "rule-hits.jsonl"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Append_and_read_latest_returns_newest_first()
    {
        RuleHitLogStore store = MakeStore();
        store.Append(new RuleHitLogEntry { Url = "https://one.example", TimestampUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) });
        store.Append(new RuleHitLogEntry { Url = "https://two.example", TimestampUtc = new DateTimeOffset(2026, 1, 1, 0, 1, 0, TimeSpan.Zero) });
        store.Append(new RuleHitLogEntry { Url = "https://three.example", TimestampUtc = new DateTimeOffset(2026, 1, 1, 0, 2, 0, TimeSpan.Zero) });

        IReadOnlyList<RuleHitLogEntry> entries = store.ReadLatest(2);

        Assert.Equal(2, entries.Count);
        Assert.Equal("https://three.example", entries[0].Url);
        Assert.Equal("https://two.example", entries[1].Url);
    }

    [Fact]
    public void Read_latest_returns_empty_when_file_missing()
    {
        RuleHitLogStore store = MakeStore();

        Assert.Empty(store.ReadLatest());
    }

    [Fact]
    public void Clear_deletes_existing_log_file()
    {
        RuleHitLogStore store = MakeStore();
        store.Append(new RuleHitLogEntry { Url = "https://example.com" });

        store.Clear();

        Assert.False(File.Exists(store.LogFilePath));
    }

    [Fact]
    public void Read_latest_skips_malformed_lines()
    {
        RuleHitLogStore store = MakeStore();
        Directory.CreateDirectory(_dir);
        File.WriteAllLines(store.LogFilePath,
        [
            "{ invalid json",
            "{\"Url\":\"https://valid.example\",\"Source\":\"open\"}",
        ]);

        IReadOnlyList<RuleHitLogEntry> entries = store.ReadLatest();

        Assert.Single(entries);
        Assert.Equal("https://valid.example", entries[0].Url);
    }
}
