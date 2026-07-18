using System.Text;
using System.Text.Json;

namespace BrowserWrangler.Core.Logging;

/// <summary>
/// Persists rule-hit entries as JSON lines under the app data directory.
/// </summary>
public sealed class RuleHitLogStore
{
    public const int MaxRetainedEntries = 500;

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
        byte[] payload = Encoding.UTF8.GetBytes(line + Environment.NewLine);
        using (var stream = new FileStream(LogFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            stream.Write(payload, 0, payload.Length);
        }

        TrimToRetentionLimit();
    }

    public IReadOnlyList<RuleHitLogEntry> ReadLatest(int maxEntries = 200)
    {
        if (maxEntries <= 0 || !File.Exists(LogFilePath))
        {
            return [];
        }

        var latestWindow = new Queue<RuleHitLogEntry>(maxEntries);
        using var stream = new FileStream(LogFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        while (reader.ReadLine() is { } line)
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
                    latestWindow.Enqueue(entry);
                    if (latestWindow.Count > maxEntries)
                    {
                        latestWindow.Dequeue();
                    }
                }
            }
            catch (JsonException)
            {
                // keep reading other lines if one row is malformed
            }
        }

        var latest = new List<RuleHitLogEntry>(latestWindow.Count);
        while (latestWindow.Count > 0)
        {
            latest.Insert(0, latestWindow.Dequeue());
        }

        return latest;
    }

    public void Clear()
    {
        if (File.Exists(LogFilePath))
        {
            try
            {
                File.Delete(LogFilePath);
            }
            catch (IOException)
            {
                // if the file is currently in use, keep the app responsive and try again later
            }
            catch (UnauthorizedAccessException)
            {
                // if the file is currently in use, keep the app responsive and try again later
            }
        }
    }

    private void TrimToRetentionLimit()
    {
        if (!File.Exists(LogFilePath))
        {
            return;
        }

        var tail = new Queue<string>(MaxRetainedEntries);
        bool exceeded = false;
        using (var readStream = new FileStream(LogFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(readStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            while (reader.ReadLine() is { } line)
            {
                tail.Enqueue(line);
                if (tail.Count > MaxRetainedEntries)
                {
                    tail.Dequeue();
                    exceeded = true;
                }
            }
        }

        if (!exceeded)
        {
            return;
        }

        using var writeStream = new FileStream(LogFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        using var writer = new StreamWriter(writeStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        foreach (string line in tail)
        {
            writer.WriteLine(line);
        }
    }
}
