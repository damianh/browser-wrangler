namespace BrowserWrangler.Core.Models;

/// <summary>
/// Which part of the click payload a rule matches against.
/// </summary>
public enum MatchLocation
{
    Url = 0,
    WindowTitle = 1,
    ProcessName = 2,
}

/// <summary>
/// Which part of the URL a rule matches against (only relevant when <see cref="MatchLocation.Url"/>).
/// </summary>
public enum MatchScope
{
    Any = 0,
    Domain = 1,
    Path = 2,
}
