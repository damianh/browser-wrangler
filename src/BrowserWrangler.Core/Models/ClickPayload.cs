namespace BrowserWrangler.Core.Models;

/// <summary>
/// Everything known about a link click: the URL plus metadata about the window/process it came from.
/// </summary>
public sealed class ClickPayload
{
    public ClickPayload() { }

    public ClickPayload(string url) => Url = url;

    public string Url { get; set; } = string.Empty;

    /// <summary>When true, the target browser should open the URL in app (frameless) mode.</summary>
    public bool AppMode { get; set; }

    /// <summary>Handle of the window the click originated from, if known.</summary>
    public nint SourceWindowHandle { get; set; }

    // Populated from the source window handle when available.
    public string WindowTitle { get; set; } = string.Empty;
    public string ProcessPath { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;

    public bool IsEmpty =>
        string.IsNullOrEmpty(Url) &&
        string.IsNullOrEmpty(WindowTitle) &&
        string.IsNullOrEmpty(ProcessName);
}
