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
        int maxSavedOrder = saved.Count > 0 ? saved.Max(b => b.SortOrder) : -1;

        foreach (Browser fresh in discovered)
        {
            Browser? old = saved.FirstOrDefault(b => b.OpenCommand == fresh.OpenCommand);
            if (old is not null)
            {
                fresh.IsHidden = old.IsHidden;
                fresh.SortOrder = old.SortOrder;

                foreach (BrowserProfile freshProfile in fresh.Profiles)
                {
                    // Profile order always follows fresh discovery (profile, then its
                    // containers, incognito last); only user data is carried over.
                    BrowserProfile? oldProfile = old.Profiles.FirstOrDefault(p => p.Id == freshProfile.Id);
                    if (oldProfile is not null)
                    {
                        freshProfile.Rules = oldProfile.Rules;
                        freshProfile.IsHidden = oldProfile.IsHidden;
                        freshProfile.UserArg = oldProfile.UserArg;
                        freshProfile.UserIconPath = oldProfile.UserIconPath;
                    }
                }
            }
            else
            {
                // new browsers go to the end instead of jumping in front of user-ordered ones
                fresh.SortOrder = ++maxSavedOrder;
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

    /// <summary>
    /// Sorts browsers and their profiles by sort order (stable) and normalizes
    /// the persisted sort orders to sequential indices.
    /// </summary>
    public static void Sort(List<Browser> browsers)
    {
        List<Browser> orderedBrowsers = [.. browsers.OrderBy(b => b.SortOrder)];
        browsers.Clear();
        browsers.AddRange(orderedBrowsers);

        for (int i = 0; i < browsers.Count; i++)
        {
            Browser browser = browsers[i];
            browser.SortOrder = i;

            List<BrowserProfile> orderedProfiles = [.. browser.Profiles.OrderBy(p => p.SortOrder)];
            browser.Profiles.Clear();
            browser.Profiles.AddRange(orderedProfiles);
            for (int j = 0; j < browser.Profiles.Count; j++)
            {
                browser.Profiles[j].SortOrder = j;
            }
        }
    }
}
