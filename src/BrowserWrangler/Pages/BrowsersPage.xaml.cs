using System.Collections.ObjectModel;
using BrowserWrangler.Services;
using BrowserWrangler.Core.Models;
using BrowserWrangler.Core.Rules;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BrowserWrangler.Pages;

public sealed partial class BrowsersPage : Page
{
    private readonly ObservableCollection<Browser> _browsers = [];
    private bool _loading;

    public BrowsersPage()
    {
        InitializeComponent();
        BrowserList.ItemsSource = _browsers;
        Rebuild();
    }

    private void Rebuild()
    {
        _loading = true;
        RebuildDefaultProfileCombo();

        _browsers.Clear();
        foreach (Browser browser in AppState.Config.Browsers)
        {
            _browsers.Add(browser);
        }

        _loading = false;
    }

    private void RebuildDefaultProfileCombo()
    {
        DefaultProfileCombo.Items.Clear();
        foreach (BrowserProfile profile in RuleMatcher.ToProfiles(AppState.Config.Browsers, skipHidden: false))
        {
            var itemPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            if (IconLoader.GetIconForProfile(profile) is { } comboIcon)
            {
                itemPanel.Children.Add(new Image
                {
                    Source = comboIcon,
                    Width = 16,
                    Height = 16,
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }

            itemPanel.Children.Add(new TextBlock
            {
                Text = profile.BestDisplayName,
                VerticalAlignment = VerticalAlignment.Center,
            });
            DefaultProfileCombo.Items.Add(new ComboBoxItem
            {
                Content = itemPanel,
                Tag = profile.LongId,
            });
            if (profile.LongId == AppState.Config.DefaultProfile)
            {
                DefaultProfileCombo.SelectedIndex = DefaultProfileCombo.Items.Count - 1;
            }
        }
    }

    private void BrowserList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        // persist the drag-and-drop order
        AppState.MutateAndSave(config =>
        {
            config.Browsers = [.. _browsers];
            for (int i = 0; i < config.Browsers.Count; i++)
            {
                config.Browsers[i].SortOrder = i;
            }
        });

        _loading = true;
        RebuildDefaultProfileCombo();
        _loading = false;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        AppState.RefreshBrowsers();
        Rebuild();
    }

    private void DefaultProfile_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        if (DefaultProfileCombo.SelectedItem is ComboBoxItem { Tag: string longId })
        {
            AppState.MutateAndSave(config => config.DefaultProfile = longId);
        }
    }
}