using System.Text;
using System.Text.Json;

namespace BrowserWrangler.Core.Logging;

/// <summary>
/// Persists rule-hit entries as JSON lines under the app data directory.
/// </summary>
public sealed class RuleHitLogStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
    };

    public RuleHitLogStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BrowserWrangler",
            "rule-hits.jsonl"))
    {
    }

    public RuleHitLogStore(string logFilePath) => LogFilePath = logFilePath;

    public string LogFilePath { get; }

    public void Append(RuleHitLogEntry entry)
    {
        string? dir = Path.GetDirectoryName(LogFilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string line = JsonSerializer.Serialize(entry, Options);
        using var stream = new FileStream(LogFilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.WriteLine(line);
    }

    public IReadOnlyList<RuleHitLogEntry> ReadLatest(int maxEntries = 200)
    {
        if (maxEntries <= 0 || !File.Exists(LogFilePath))
        {
            return [];
        }

        var entries = new List<RuleHitLogEntry>();
        foreach (string line in File.ReadLines(LogFilePath))
        {
            if (line.Length == 0)
            {
                continue;
            }

            try
            {
                RuleHitLogEntry? entry = JsonSerializer.Deserialize<RuleHitLogEntry>(line, Options);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            }
            catch (JsonException)
            {
                // keep reading other lines if one row is malformed
            }
        }

        int start = Math.Max(0, entries.Count - maxEntries);
        var latest = new List<RuleHitLogEntry>(entries.Count - start);
        for (int i = entries.Count - 1; i >= start; i--)
        {
            latest.Add(entries[i]);
        }

        return latest;
    }

    public void Clear()
    {
        if (File.Exists(LogFilePath))
        {
            File.Delete(LogFilePath);
        }
    }
}
