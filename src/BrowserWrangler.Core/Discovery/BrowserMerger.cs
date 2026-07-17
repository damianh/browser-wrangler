using BrowserWrangler.Core.Models;

namespace BrowserWrangler.Core.Discovery;

/// <summary>
/// Merges a freshly discovered browser set with the previously saved one,
/// preserving user data (rules, hidden flags, sort order, custom browsers).
/// Mirrors bt's browser::merge.
/// </summary>
public static class BrowserMerger
{
    public static List<Browser> Merge(List<Browser> discovered, List<Browser> saved)
    {
        var result = new List<Browser>();

        foreach (Browser fresh in discovered)
        {
            Browser? old = saved.FirstOrDefault(b => b.OpenCommand == fresh.OpenCommand);
            if (old is not null)
            {
                fresh.IsHidden = old.IsHidden;
                fresh.SortOrder = old.SortOrder;

                foreach (BrowserProfile freshProfile in fresh.Profiles)
                {
                    BrowserProfile? oldProfile = old.Profiles.FirstOrDefault(p => p.Id == freshProfile.Id);
                    if (oldProfile is not null)
                    {
                        freshProfile.Rules = oldProfile.Rules;
                        freshProfile.IsHidden = oldProfile.IsHidden;
                        freshProfile.SortOrder = oldProfile.SortOrder;
                        freshProfile.UserArg = oldProfile.UserArg;
                        freshProfile.UserIconPath = oldProfile.UserIconPath;
                    }
                }
            }

            result.Add(fresh);
        }

        // keep user-defined browsers that discovery does not produce
        foreach (Browser old in saved.Where(b => !b.IsAutoDiscovered))
        {
            if (!result.Any(b => b.OpenCommand == old.OpenCommand))
            {
                result.Add(old);
            }
        }

        Sort(result);
        return result;
    }

    /// <summary>Sorts browsers and their profiles by sort order (stable).</summary>
    public static void Sort(List<Browser> browsers)
    {
        browsers.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
        foreach (Browser browser in browsers)
        {
            browser.Profiles.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
        }
    }
}
