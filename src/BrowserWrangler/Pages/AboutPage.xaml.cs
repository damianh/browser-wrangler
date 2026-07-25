using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using BrowserWrangler.Core.Updates;
using BrowserWrangler.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;

namespace BrowserWrangler.Pages;

public sealed partial class AboutPage : Page
{
    private const string NewIssueUrl = "https://github.com/damianh/browser-wrangler/issues/new";
    private const string BugReportTemplate = "bug_report.yml";

    /// <summary>
    /// Long prefilled URLs get rejected by servers and proxies. The diagnostics block is a
    /// few hundred characters, so this only guards against something unexpected.
    /// </summary>
    private const int MaxPrefilledIssueUrlLength = 6000;

    public AboutPage()
    {
        InitializeComponent();

        VersionText.Text = $"Version {DiagnosticsInfo.VersionDisplay}  \u00b7  {RuntimeInformation.ProcessArchitecture}";
        CopyrightText.Text = DiagnosticsInfo.Copyright;
        LoadAppIcon();
        BuildAppInfoGrid();
    }

    private void LoadAppIcon()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "logo-256.png");
        if (!File.Exists(path))
        {
            return;
        }

        AppIcon.Source = new BitmapImage(new Uri(path));
        AppIcon.Visibility = Visibility.Visible;
    }

    private void BuildAppInfoGrid()
    {
        IReadOnlyList<DiagnosticsEntry> entries = DiagnosticsInfo.Entries;
        for (int row = 0; row < entries.Count; row++)
        {
            DiagnosticsEntry entry = entries[row];
            AppInfoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock
            {
                Text = entry.Label,
                Opacity = 0.7,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetRow(label, row);
            Grid.SetColumn(label, 0);
            AppInfoGrid.Children.Add(label);

            var value = new TextBlock
            {
                Text = entry.Value,
                TextWrapping = TextWrapping.WrapWholeWords,
                IsTextSelectionEnabled = true,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetRow(value, row);
            Grid.SetColumn(value, 1);
            AppInfoGrid.Children.Add(value);

            if (entry.FolderPath is not { Length: > 0 } folder || !Directory.Exists(folder))
            {
                continue;
            }

            var open = new HyperlinkButton
            {
                Content = "Open folder",
                Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            open.Click += (_, _) => OpenFolder(folder);
            Grid.SetRow(open, row);
            Grid.SetColumn(open, 2);
            AppInfoGrid.Children.Add(open);
        }
    }

    private static void OpenFolder(string folder)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Opening a folder is a convenience; failing to do so should never crash the app.
        }
    }

    private void CopyDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetText(DiagnosticsInfo.ToPlainText());
        Clipboard.SetContent(package);

        ShowStatus("App info copied to the clipboard.");
    }

    private void ReportIssue_Click(object sender, RoutedEventArgs e)
    {
        string url = BuildIssueUrl();
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowStatus($"Could not open the browser: {ex.Message}");
        }
    }

    /// <summary>
    /// Builds a prefilled bug report. The 'app-info' key must match the field id in
    /// .github/ISSUE_TEMPLATE/bug_report.yml or the prefill is silently ignored.
    /// </summary>
    private static string BuildIssueUrl()
    {
        var builder = new StringBuilder(NewIssueUrl)
            .Append("?template=").Append(BugReportTemplate)
            .Append("&app-info=").Append(Uri.EscapeDataString(DiagnosticsInfo.ToPlainText()));

        return builder.Length <= MaxPrefilledIssueUrlLength
            ? builder.ToString()
            : $"{NewIssueUrl}?template={BugReportTemplate}";
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        UpdateProgress.IsActive = true;
        UpdateLink.Visibility = Visibility.Collapsed;
        ShowStatus("Checking for updates\u2026");

        try
        {
            using HttpClient client = UpdateChecker.CreateHttpClient(DiagnosticsInfo.AppVersion);
            var checker = new UpdateChecker(client);
            UpdateCheckResult result = await checker.CheckAsync(DiagnosticsInfo.AppVersion);

            ShowStatus(result.Message);
            if (result.ReleaseUrl is { Length: > 0 } releaseUrl && result.Status != UpdateCheckStatus.Failed)
            {
                UpdateLink.NavigateUri = new Uri(releaseUrl);
                UpdateLink.Content = result.Status == UpdateCheckStatus.UpdateAvailable
                    ? $"Download version {result.LatestVersion}"
                    : "Open the release page";
                UpdateLink.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            ShowStatus($"The update check failed: {ex.Message}");
        }
        finally
        {
            UpdateProgress.IsActive = false;
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private void ShowStatus(string message)
    {
        UpdateStatusText.Text = message;
        UpdateStatusText.Visibility = Visibility.Visible;
    }
}
