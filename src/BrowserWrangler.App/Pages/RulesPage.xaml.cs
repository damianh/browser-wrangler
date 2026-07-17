using BrowserWrangler.App.Services;
using BrowserWrangler.Core.Models;
using BrowserWrangler.Core.Rules;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BrowserWrangler.App.Pages;

public sealed partial class RulesPage : Page
{
    private readonly List<BrowserProfile> _profiles;

    public RulesPage()
    {
        InitializeComponent();
        _profiles = RuleMatcher.ToProfiles(AppState.Config.Browsers, skipHidden: false);
        foreach (BrowserProfile profile in _profiles)
        {
            ProfileCombo.Items.Add(profile.BestDisplayName);
        }

        if (_profiles.Count > 0)
        {
            ProfileCombo.SelectedIndex = 0;
        }
    }

    private BrowserProfile? SelectedProfile =>
        ProfileCombo.SelectedIndex >= 0 && ProfileCombo.SelectedIndex < _profiles.Count
            ? _profiles[ProfileCombo.SelectedIndex]
            : null;

    private void Profile_Changed(object sender, SelectionChangedEventArgs e)
    {
        RulesBox.Text = SelectedProfile is { } p
            ? string.Join(Environment.NewLine, p.Rules.Select(r => r.ToLine()))
            : string.Empty;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile is not { } profile)
        {
            return;
        }

        profile.Rules = RulesBox.Text
            .Split('\n')
            .Select(l => l.TrimEnd('\r').Trim())
            .Where(l => l.Length > 0)
            .Select(MatchRule.Parse)
            .ToList();
        AppState.Save();
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        string url = TestUrlBox.Text.Trim();
        if (url.Length == 0)
        {
            return;
        }

        var matches = RuleMatcher.Match(
            AppState.Config.Browsers, new ClickPayload(url), AppState.Config.DefaultProfile);

        string text = matches.Count == 0
            ? "No browsers configured."
            : string.Join("\n", matches.Select(m =>
                $"{m.Profile.BestDisplayName}  \u2190  {(m.Rule.IsFallback ? "(default fallback)" : m.Rule.ToLine())}"));

        var dialog = new ContentDialog
        {
            Title = "Match result",
            Content = text,
            CloseButtonText = "Close",
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
    }
}
