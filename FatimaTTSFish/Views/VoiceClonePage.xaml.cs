using System.Collections.ObjectModel;
using FatimaTTS.Models;
using FatimaTTS.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace FatimaTTS.Views;

public partial class VoiceClonePage : Page
{
    private readonly FishTtsService    _tts;
    private readonly CredentialService _credentials;

    private readonly ObservableCollection<string> _sampleFiles = [];
    private string? _lastClonedReferenceId;

    public VoiceClonePage()
    {
        InitializeComponent();
        _tts         = App.Services.GetRequiredService<FishTtsService>();
        _credentials = App.Services.GetRequiredService<CredentialService>();

        SampleFilesList.ItemsSource = _sampleFiles;
        _sampleFiles.CollectionChanged += (_, _) =>
            NoSamplesText.Visibility = _sampleFiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        Loaded += (_, _) => _ = RefreshMyVoicesAsync();
    }

    // ── Sample file picking ─────────────────────────────────────────────────

    private void ChooseSamplesButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title       = "Choose audio samples",
            Filter      = "Audio files|*.wav;*.mp3;*.m4a;*.opus|All files|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;

        foreach (var path in dlg.FileNames)
        {
            if (_sampleFiles.Count >= 20)
            {
                MessageBox.Show("Fish.audio accepts at most 20 sample files per clone.",
                    "Too Many Files", MessageBoxButton.OK, MessageBoxImage.Information);
                break;
            }
            if (!_sampleFiles.Contains(path))
                _sampleFiles.Add(path);
        }
    }

    private void RemoveSample_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path })
            _sampleFiles.Remove(path);
    }

    // ── Clone ────────────────────────────────────────────────────────────

    private async void CloneButton_Click(object sender, RoutedEventArgs e)
    {
        var apiKey = _credentials.LoadApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            MessageBox.Show("Please add your Fish.audio API key in Settings first.",
                "No API Key", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var title = TitleBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            MessageBox.Show("Please give this voice a title.",
                "No Title", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_sampleFiles.Count == 0)
        {
            MessageBox.Show("Please choose at least one audio sample.",
                "No Samples", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var visibility = (VisibilityCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "private";
        var description = DescriptionBox.Text.Trim();

        CloneButton.IsEnabled = false;
        CloneButton.Content   = new TextBlock { Text = "Cloning… this can take a moment", FontSize = 13 };
        ResultPanel.Visibility = Visibility.Collapsed;

        try
        {
            var voice = await _tts.CloneVoiceAsync(
                apiKey,
                title,
                _sampleFiles.ToList(),
                description: string.IsNullOrWhiteSpace(description) ? null : description,
                visibility: visibility,
                enhanceAudioQuality: EnhanceQualityCheck.IsChecked == true);

            _lastClonedReferenceId = voice.Id;

            ResultTitleText.Text = string.IsNullOrEmpty(voice.Id)
                ? "⚠  Cloned, but no reference ID came back — check the log."
                : $"✓  \"{voice.Title}\" cloned — state: {voice.State ?? "unknown"}";
            ResultIdBox.Text       = voice.Id;
            ResultPanel.Visibility = Visibility.Visible;

            await RefreshMyVoicesAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Voice cloning failed:\n{ex.Message}",
                "Clone Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            CloneButton.IsEnabled = true;
            CloneButton.Content   = "✦  Clone Voice";
        }
    }

    private void CopyResultId_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(ResultIdBox.Text))
            Clipboard.SetText(ResultIdBox.Text);
    }

    private void UseInGenerate_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastClonedReferenceId)) return;
        if (Window.GetWindow(this) is MainWindow mw)
            mw.NavigateToPage("generate", _lastClonedReferenceId);
    }

    // ── Your voices list ─────────────────────────────────────────────────

    private async void RefreshMyVoices_Click(object sender, RoutedEventArgs e)
        => await RefreshMyVoicesAsync();

    private async Task RefreshMyVoicesAsync()
    {
        var apiKey = _credentials.LoadApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            MyVoicesList.ItemsSource   = null;
            MyVoicesEmptyText.Text     = "Add an API key in Settings to see your voices.";
            MyVoicesEmptyText.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            var page = await _tts.ListVoicesAsync(apiKey);
            var vms  = page.Items.Select(v => new MyVoiceViewModel(v)).ToList();

            MyVoicesList.ItemsSource     = vms;
            MyVoicesEmptyText.Text       = "No voices yet";
            MyVoicesEmptyText.Visibility = vms.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            MyVoicesList.ItemsSource     = null;
            MyVoicesEmptyText.Text       = $"Couldn't load voices: {ex.Message}";
            MyVoicesEmptyText.Visibility = Visibility.Visible;
        }
    }

    private void CopyVoiceId_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id } && !string.IsNullOrEmpty(id))
            Clipboard.SetText(id);
    }
}

// ── Voice list item view model ────────────────────────────────────────────

public class MyVoiceViewModel(FishVoice voice)
{
    public string Id    => voice.Id;
    public string Title => string.IsNullOrWhiteSpace(voice.Title) ? voice.Id : voice.Title;
    public string StateAndVisibility =>
        $"{voice.State ?? "unknown"} · {voice.Visibility ?? "private"}";
}
