using FatimaTTS.Services;
using Microsoft.Extensions.DependencyInjection;


namespace FatimaTTS.Views;

public partial class MainWindow : Window
{
    private readonly ThemeService         _theme;
    private readonly CredentialService    _credentials;
    private readonly SettingsService      _settingsService;
    private readonly GitHubUpdateService  _updateService;

    // Track which nav accent border is active
    private Border? _activeAccent;
    private Button? _activeNavBtn;

    private UpdateInfo? _pendingUpdate;

    public MainWindow()
    {
        InitializeComponent();

        _theme           = App.Services.GetRequiredService<ThemeService>();
        _credentials     = App.Services.GetRequiredService<CredentialService>();
        _settingsService = App.Services.GetRequiredService<SettingsService>();
        _updateService   = App.Services.GetRequiredService<GitHubUpdateService>();

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Sidebar version from the assembly (never hardcode — it goes stale)
        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        if (v is not null) VersionText.Text = $"v{v.Major}.{v.Minor}.{v.Build}";

        // Apply saved theme to toggle state
        var saved = _settingsService.Load().Theme;
        ThemeToggle.IsChecked = (saved == "Dark");

        // Refresh API key status indicator
        RefreshApiStatus();

        // Default page: Generate Speech
        NavigateTo("generate");

        // Silent background update check — never blocks startup, never nags
        // for a version the user already dismissed.
        _ = CheckForUpdatesAsync();
    }

    // ── Update check ─────────────────────────────────────────────────────

    private async Task CheckForUpdatesAsync()
    {
        var info = await _updateService.CheckAsync();
        if (info is null) return;

        var settings = _settingsService.Load();
        if (settings.DismissedUpdateVersion == info.Version) return;

        _pendingUpdate = info;
        UpdateBannerText.Text   = $"Fatima TTS (Fish) v{info.Version} is available — you're on v{GitHubUpdateService.CurrentVersion}";
        UpdateBanner.Visibility = Visibility.Visible;
    }

    private async void UpdateBannerDownload_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate is null) return;

        if (!_pendingUpdate.IsInstallerAvailable)
        {
            // No MSI asset on the release (e.g. CI hasn't had one uploaded yet) —
            // fall back to sending the user to the releases page rather than
            // failing silently.
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    _pendingUpdate.ReleasesUrl) { UseShellExecute = true });
            }
            catch { /* browser launch failure is non-fatal */ }
            return;
        }

        UpdateBannerActions.Visibility  = Visibility.Collapsed;
        UpdateBannerProgress.Visibility = Visibility.Visible;
        UpdateBannerProgress.Value      = 0;
        UpdateBannerText.Text           = "Downloading update… 0%";

        var progress = new Progress<int>(pct =>
        {
            UpdateBannerProgress.Value = pct;
            UpdateBannerText.Text      = $"Downloading update… {pct}%";
        });

        try
        {
            var msiPath = await _updateService.DownloadInstallerAsync(_pendingUpdate.DownloadUrl, progress);

            UpdateBannerText.Text = "Download complete — launching installer…";

            // Launch the installer (Windows shows the UAC elevation prompt, then
            // the normal MSI wizard) and close this instance so its files aren't
            // locked while the installer replaces them.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(msiPath)
            { UseShellExecute = true });

            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            UpdateBannerProgress.Visibility = Visibility.Collapsed;
            UpdateBannerActions.Visibility  = Visibility.Visible;
            UpdateBannerText.Text = $"Update download failed: {ex.Message}";
        }
    }

    private void UpdateBannerDismiss_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate is not null)
        {
            var settings = _settingsService.Load();
            settings.DismissedUpdateVersion = _pendingUpdate.Version;
            _settingsService.Save(settings);
        }
        UpdateBanner.Visibility = Visibility.Collapsed;
    }

    // Force correct TextBox foreground after each page load — WPF bug workaround
    // TextBoxView ignores template foreground in some Windows versions
    private void FixTextBoxForegrounds()
    {
        Dispatcher.InvokeAsync(() =>
        {
            var brush = (Brush)FindResource("TextPrimaryBrush");
            FixTextBoxesInVisual(ContentFrame, brush);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static void FixTextBoxesInVisual(DependencyObject parent, Brush brush)
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is TextBox tb)
                tb.Foreground = brush;
            else
                FixTextBoxesInVisual(child, brush);
        }
    }

    // ── API key status indicator ──────────────────────────────────────────

    public void RefreshApiStatus()
    {
        bool hasKey = _credentials.HasApiKey();
        ApiStatusDot.Fill  = hasKey
            ? new SolidColorBrush(Color.FromRgb(0x1D, 0x9E, 0x75))   // green
            : new SolidColorBrush(Color.FromRgb(0xE2, 0x4B, 0x4A));  // red
        ApiStatusText.Text = hasKey ? "API key active" : "No API key";
    }

    private void ThemeToggle_Checked(object sender, RoutedEventArgs e)
    {
        _theme.ApplyAndSave("Dark");
        FixTextBoxForegrounds();
    }

    private void ThemeToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        _theme.ApplyAndSave("Light");
        FixTextBoxForegrounds();
    }

    // ── Navigation ───────────────────────────────────────────────────────

    // Called by pages that need to navigate the shell (e.g. "Go to My Jobs")
    public void NavigateToPage(string page) => NavigateTo(page);
    public void NavigateToPage(string page, string parameter) => NavigateTo(page, parameter);

    private void NavDashboard_Click(object sender, RoutedEventArgs e)
        => NavigateTo("dashboard");

    private void NavSettings_Click(object sender, RoutedEventArgs e)
        => NavigateTo("settings");

    private void NavGenerate_Click(object sender, RoutedEventArgs e)
        => NavigateTo("generate");

    private void NavBatch_Click(object sender, RoutedEventArgs e)
        => NavigateTo("batch");

    private void NavAbout_Click(object sender, RoutedEventArgs e)
        => NavigateTo("about");

    private void NavMyJobs_Click(object sender, RoutedEventArgs e)
        => NavigateTo("myjobs");

    private void NavClone_Click(object sender, RoutedEventArgs e)
        => NavigateTo("voiceclone");

    private void NavigateTo(string page, string? parameter = null)
    {
        // Reset previous active state
        if (_activeNavBtn is not null)
            _activeNavBtn.Style = (Style)FindResource("NavButtonStyle");
        if (_activeAccent is not null)
            _activeAccent.Visibility = Visibility.Collapsed;

        Border accent;
        Button btn;
        Page   view;

        switch (page)
        {
            case "dashboard":
                accent = AccentDashboard;   btn = NavDashboard;    view = CreatePage<DashboardPage>();    break;
            case "settings":
                accent = AccentSettings;    btn = NavSettings;     view = CreatePage<SettingsPage>();     break;
            case "about":
                accent = AccentAbout;       btn = NavAbout;        view = CreatePage<AboutPage>();        break;
            case "batch":
                accent = AccentBatch;       btn = NavBatch;        view = CreatePage<BatchGeneratePage>(); break;
            case "myjobs":
                accent = AccentMyJobs;      btn = NavMyJobs;       view = CreatePage<MyJobsPage>();       break;
            case "batchdetail":
                accent = AccentBatch;       btn = NavBatch;
                var detailPage = new BatchDetailPage(parameter ?? "");
                view = detailPage;
                break;
            case "voiceclone":
                accent = AccentClone;       btn = NavClone;        view = CreatePage<VoiceClonePage>();   break;
            default: // "generate"
                accent = AccentGenerate;    btn = NavGenerate;
                var genPage = CreatePage<GenerateSpeechPage>();
                if (!string.IsNullOrWhiteSpace(parameter))
                    genPage.PresetReferenceId = parameter;
                view = genPage;
                break;
        }

        accent.Visibility = Visibility.Visible;
        btn.Style         = (Style)FindResource("NavButtonActiveStyle");
        _activeAccent     = accent;
        _activeNavBtn     = btn;

        ContentFrame.Navigate(view);
        FixTextBoxForegrounds();
    }

    private static T CreatePage<T>() where T : Page
        => App.Services.GetRequiredService<T>();
}
