using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using FatimaTTS.Services;
using Microsoft.Extensions.DependencyInjection;

namespace FatimaTTS.Views;

public partial class AboutPage : Page
{
    private readonly GitHubUpdateService _updateService;
    private UpdateInfo? _lastCheckResult;

    public AboutPage()
    {
        InitializeComponent();
        _updateService = App.Services.GetRequiredService<GitHubUpdateService>();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Version from assembly
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionLabel.Text = version is not null
            ? $"Version {version.Major}.{version.Minor}.{version.Build}"
            : "Version 1.0.0";
        WhatsNewLabel.Text = version is not null
            ? $"WHAT'S NEW IN v{version.Major}.{version.Minor}.{version.Build}"
            : "WHAT'S NEW";

        // Runtime
        DotNetVersion.Text = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;

        // OS
        OsVersion.Text = System.Runtime.InteropServices.RuntimeInformation.OSDescription;

        // Data folder
        var dataDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FatimaTTSFish");
        DataFolder.Text = dataDir;
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled  = false;
        DownloadUpdateButton.Visibility = Visibility.Collapsed;
        UpdateStatusText.Text        = "Checking…";
        UpdateStatusText.Foreground  = (Brush)FindResource("TextMutedBrush");

        var info = await _updateService.CheckAsync();
        _lastCheckResult = info;

        if (info is null)
        {
            UpdateStatusText.Text       = $"✓ You're up to date (v{GitHubUpdateService.CurrentVersion})";
            UpdateStatusText.Foreground = (Brush)FindResource("SuccessBrush");
        }
        else
        {
            UpdateStatusText.Text          = $"Update available: v{info.Version}";
            UpdateStatusText.Foreground    = (Brush)FindResource("AccentBrush");
            DownloadUpdateButton.Visibility = Visibility.Visible;
        }

        CheckUpdatesButton.IsEnabled = true;
    }

    private async void DownloadUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_lastCheckResult is null) return;

        if (!_lastCheckResult.IsInstallerAvailable)
        {
            OpenUrl(_lastCheckResult.ReleasesUrl);
            return;
        }

        DownloadUpdateButton.IsEnabled = false;
        UpdateProgressBar.Visibility   = Visibility.Visible;
        UpdateProgressBar.Value        = 0;
        UpdateStatusText.Text          = "Downloading… 0%";
        UpdateStatusText.Foreground    = (Brush)FindResource("TextMutedBrush");

        var progress = new Progress<int>(pct =>
        {
            UpdateProgressBar.Value = pct;
            UpdateStatusText.Text   = $"Downloading… {pct}%";
        });

        try
        {
            var msiPath = await _updateService.DownloadInstallerAsync(_lastCheckResult.DownloadUrl, progress);

            UpdateStatusText.Text = "Launching installer…";

            // Launch the installer (Windows shows the UAC elevation prompt, then
            // the normal MSI wizard) and close this instance so its files aren't
            // locked while the installer replaces them.
            Process.Start(new ProcessStartInfo(msiPath) { UseShellExecute = true });
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            UpdateProgressBar.Visibility   = Visibility.Collapsed;
            DownloadUpdateButton.IsEnabled = true;
            UpdateStatusText.Text          = $"Download failed: {ex.Message}";
            UpdateStatusText.Foreground    = (Brush)FindResource("DangerBrush");
        }
    }

    private void OpenFishDocs_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://docs.fish.audio/");
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        var dataDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FatimaTTSFish");

        if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
        Process.Start(new ProcessStartInfo("explorer.exe", dataDir) { UseShellExecute = true });
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { /* fail silently */ }
    }
}
