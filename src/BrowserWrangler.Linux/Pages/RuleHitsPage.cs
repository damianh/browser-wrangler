using System.Globalization;
using Avalonia.Controls;
using BrowserWrangler.Core.Logging;
using BrowserWrangler.Linux.Services;

namespace BrowserWrangler.Linux.Pages;

internal sealed class RuleHitsPage : Page
{
    private readonly RuleHitLogStore _store = new();
    private readonly StackPanel _list = new() { Spacing = 6 };

    public RuleHitsPage()
        : base("Rule hits")
    {
        AddHeaderAction(Ui.Row(
            8,
            Ui.Button("Refresh", Build),
            Ui.Button("Clear log", () =>
            {
                _store.Clear();
                Build();
            })));
        Body.Children.Add(Ui.Caption("Shows recent routing decisions when logging is enabled in Settings."));
        Body.Children.Add(_list);
        Build();
    }

    private void Build()
    {
        _list.Children.Clear();
        IReadOnlyList<RuleHitLogEntry> entries = _store.ReadLatest();
        if (entries.Count == 0)
        {
            _list.Children.Add(Ui.Caption(AppState.Config.LogRuleHits
                ? "No rule-hit entries yet."
                : "No rule-hit entries yet. Turn on \"Record rule hits\" in Settings."));
            return;
        }

        foreach (RuleHitLogEntry entry in entries)
        {
            string when = entry.TimestampUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
            string rule = entry.IsFallback ? "default" : entry.RuleText;
            var url = Ui.Text(entry.Url, 13);
            url.TextWrapping = Avalonia.Media.TextWrapping.NoWrap;
            url.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
            ToolTip.SetTip(url, entry.Url);
            _list.Children.Add(Ui.Card(Ui.Column(
                2,
                url,
                Ui.Caption($"{when} \u00b7 {entry.ProfileDisplayName}  \u2190  {rule} \u00b7 {entry.Source}"))));
        }
    }
}
