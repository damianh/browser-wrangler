using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using BrowserWrangler.Core.Configuration;
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
        Loaded += AboutPage_Loaded;
        Unloaded += AboutPage_Unloaded;
    }

    private void AboutPage_Loaded(object sender, RoutedEventArgs e)
    {
        AppState.Updates.SnapshotChanged += Updates_SnapshotChanged;
        ApplyUpdateSnapshot();
    }

    private void AboutPage_Unloaded(object sender, RoutedEventArgs e) =>
        AppState.Updates.SnapshotChanged -= Updates_SnapshotChanged;

    private void Updates_SnapshotChanged(object? sender, EventArgs e) =>
        _ = DispatcherQueue.TryEnqueue(ApplyUpdateSnapshot);

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
            _ = await AppState.Updates.CheckNowAsync();
            ApplyUpdateSnapshot();
        }
        catch (OperationCanceledException)
        {
            ShowStatus("The update check was canceled.");
        }
        catch (Exception ex)
        {
            ShowStatus($"Could not check for updates: {ex.Message}");
        }
        finally
        {
            UpdateProgress.IsActive = false;
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetValidPendingInstaller(out string installerPath, out string versionText))
        {
            ShowStatus("No downloaded installer is available.");
            InstallUpdateButton.Visibility = Visibility.Collapsed;
            return;
        }
        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Install update now?",
            Content = $"Version {versionText} is downloaded. Browser Wrangler will close when you run the installer.",
            PrimaryButtonText = "Run installer",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            Process? installerProcess = Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
            if (installerProcess is null)
            {
                ShowStatus("Could not start installer.");
                return;
            }

            ShowStatus($"Started installer for version {versionText}.");
            App.Current?.Exit();
        }
        catch (Win32Exception ex)
        {
            ShowStatus($"Could not start installer: {ex.Message}");
        }
        catch (FileNotFoundException ex)
        {
            ShowStatus($"Installer file was not found: {ex.Message}");
        }
    }

    private void ApplyUpdateSnapshot()
    {
        UpdateAutomationSnapshot snapshot = AppState.Updates.Snapshot;
        if (snapshot.StatusMessage.Length > 0)
        {
            ShowStatus(snapshot.StatusMessage);
        }

        UpdateProgress.IsActive = snapshot.IsChecking || snapshot.IsDownloading;
        InstallUpdateButton.Visibility = TryGetValidPendingInstaller(out _, out _) ? Visibility.Visible : Visibility.Collapsed;

        UpdateCheckResult? result = snapshot.LastCheckResult;
        if (result is null || result.Status == UpdateCheckStatus.Failed || result.ReleaseUrl is not { Length: > 0 } releaseUrl)
        {
            UpdateLink.Visibility = Visibility.Collapsed;
            return;
        }

        UpdateLink.NavigateUri = new Uri(releaseUrl);
        UpdateLink.Content = result.Status == UpdateCheckStatus.UpdateAvailable
            ? $"Open version {result.LatestVersion} release notes"
            : "Open the release page";
        UpdateLink.Visibility = Visibility.Visible;
    }

    private void ShowStatus(string message)
    {
        UpdateStatusText.Text = message;
        UpdateStatusText.Visibility = Visibility.Visible;
    }

    private static bool TryGetValidPendingInstaller(out string installerPath, out string installerVersion)
    {
        (string pendingInstallerPath, string pendingInstallerVersion) = AppState.ReadConfig(config =>
            (config.Updates.PendingInstallerPath, config.Updates.PendingInstallerVersion));

        installerPath = string.Empty;
        installerVersion = string.Empty;
        if (pendingInstallerPath.Length == 0 && pendingInstallerVersion.Length == 0)
        {
            return false;
        }

        string normalizedPath = string.Empty;
        bool valid = pendingInstallerPath.Length > 0
            && pendingInstallerVersion.Length > 0
            && ConfigStore.TryNormalizeManagedPendingInstallerPath(pendingInstallerPath, out normalizedPath)
            && File.Exists(normalizedPath);
        if (!valid)
        {
            TryClearInvalidPendingInstallerMetadata();
            return false;
        }

        installerPath = normalizedPath;
        installerVersion = pendingInstallerVersion;
        return true;
    }

    private static void TryClearInvalidPendingInstallerMetadata()
    {
        try
        {
            AppState.MutateAndSave(config =>
            {
                config.Updates.PendingInstallerPath = string.Empty;
                config.Updates.PendingInstallerVersion = string.Empty;
            });
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
