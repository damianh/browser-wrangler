using BrowserWrangler.Core.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BrowserWrangler.Pages;

public sealed partial class RuleHitsPage : Page
{
    private readonly RuleHitLogStore _store = new();

    public RuleHitsPage()
    {
        InitializeComponent();
        Rebuild();
    }

    private void Rebuild()
    {
        EntriesPanel.Children.Clear();
        IReadOnlyList<RuleHitLogEntry> entries = _store.ReadLatest(200);
        EmptyStateText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (RuleHitLogEntry entry in entries)
        {
            EntriesPanel.Children.Add(BuildRow(entry));
        }
    }

    private static Border BuildRow(RuleHitLogEntry entry)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock
        {
            Text = $"{entry.TimestampUtc.LocalDateTime:G}  -  {entry.ProfileDisplayName}",
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock
        {
            Text = entry.Url,
            Opacity = 0.85,
            TextWrapping = TextWrapping.WrapWholeWords,
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"{FormatSource(entry.Source)}  |  {entry.RuleText}",
            FontSize = 12,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap,
        });

        return new Border
        {
            Child = panel,
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(6),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
        };
    }

    private static string FormatSource(string source) => source switch
    {
        "open" => "Direct open",
        "picker" => "Picker selection",
        _ => source,
    };

    private void Refresh_Click(object sender, RoutedEventArgs e) => Rebuild();

    private async void Clear_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Clear rule-hit log?",
            Content = "This permanently removes all logged rule-hit entries.",
            PrimaryButtonText = "Clear",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            _store.Clear();
            Rebuild();
        }
    }
}
