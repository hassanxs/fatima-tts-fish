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
    private List<MyVoiceViewModel> _myVoices = [];

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
            _myVoices = page.Items.Select(v => new MyVoiceViewModel(v)).ToList();

            MyVoicesList.ItemsSource     = _myVoices;
            MyVoicesEmptyText.Text       = "No voices yet";
            MyVoicesEmptyText.Visibility = _myVoices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            _myVoices = [];
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

    // ── Voice management: rename / re-tag visibility / delete ─────────────

    private MyVoiceViewModel? FindVoice(object sender)
        => sender is Button { Tag: string id } ? _myVoices.FirstOrDefault(v => v.Id == id) : null;

    private void EditVoice_Click(object sender, RoutedEventArgs e)
    {
        var vm = FindVoice(sender);
        if (vm is null) return;

        // Close any other open editors first
        foreach (var other in _myVoices.Where(v => v.IsEditing))
            other.IsEditing = false;

        vm.EditTitle           = vm.Title;
        vm.EditVisibilityIndex = vm.RawVisibility == "unlist" ? 1 : 0;
        vm.IsEditing           = true;
    }

    private void CancelEditVoice_Click(object sender, RoutedEventArgs e)
    {
        var vm = FindVoice(sender);
        if (vm is not null) vm.IsEditing = false;
    }

    private async void SaveEditVoice_Click(object sender, RoutedEventArgs e)
    {
        var vm = FindVoice(sender);
        if (vm is null) return;

        var apiKey = _credentials.LoadApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            MessageBox.Show("Please add your Fish.audio API key in Settings first.",
                "No API Key", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var newTitle      = vm.EditTitle.Trim();
        var newVisibility = vm.EditVisibilityIndex == 1 ? "unlist" : "private";
        if (string.IsNullOrWhiteSpace(newTitle))
        {
            MessageBox.Show("Title can't be empty.", "No Title",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        vm.IsBusy = true;
        try
        {
            var updated = await _tts.UpdateVoiceAsync(apiKey, vm.Id, title: newTitle, visibility: newVisibility);
            vm.ApplyUpdate(updated);
            vm.IsEditing = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't update voice:\n{ex.Message}",
                "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            vm.IsBusy = false;
        }
    }

    private async void DeleteVoice_Click(object sender, RoutedEventArgs e)
    {
        var vm = FindVoice(sender);
        if (vm is null) return;

        var result = MessageBox.Show(
            $"Delete \"{vm.Title}\"?\nThis permanently removes the voice model from your Fish.audio account. " +
            "Any job still referencing this Reference ID will start failing.",
            "Delete Voice", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        var apiKey = _credentials.LoadApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            MessageBox.Show("Please add your Fish.audio API key in Settings first.",
                "No API Key", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            await _tts.DeleteVoiceAsync(apiKey, vm.Id);
            await RefreshMyVoicesAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't delete voice:\n{ex.Message}",
                "Delete Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

// ── Voice list item view model ────────────────────────────────────────────

public class MyVoiceViewModel : System.ComponentModel.INotifyPropertyChanged
{
    private string  _title;
    private string? _state;
    private string? _visibility;
    private bool    _isEditing;
    private bool    _isBusy;

    public string  Id            { get; }
    public string  RawVisibility => _visibility ?? "private";

    public string Title
    {
        get => _title;
        private set { _title = value; OnChanged(nameof(Title)); }
    }

    public string StateAndVisibility => $"{_state ?? "unknown"} · {_visibility ?? "private"}";

    public bool IsEditing
    {
        get => _isEditing;
        set { _isEditing = value; OnChanged(nameof(IsEditing)); OnChanged(nameof(EditPanelVisibility)); }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnChanged(nameof(IsBusy)); }
    }

    public Visibility EditPanelVisibility => _isEditing ? Visibility.Visible : Visibility.Collapsed;

    public string EditTitle { get; set; } = "";

    // 0 = private, 1 = unlist — matches the two ComboBoxItems in the edit panel
    public int EditVisibilityIndex { get; set; }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged(string name) => PropertyChanged?.Invoke(this, new(name));

    public MyVoiceViewModel(FishVoice voice)
    {
        Id          = voice.Id;
        _title      = string.IsNullOrWhiteSpace(voice.Title) ? voice.Id : voice.Title;
        _state      = voice.State;
        _visibility = voice.Visibility;
        EditTitle   = _title;
    }

    /// <summary>Applies a fresh server response (e.g. after a successful rename) in place.</summary>
    public void ApplyUpdate(FishVoice voice)
    {
        Title       = string.IsNullOrWhiteSpace(voice.Title) ? voice.Id : voice.Title;
        _state      = voice.State ?? _state;
        _visibility = voice.Visibility ?? _visibility;
        OnChanged(nameof(StateAndVisibility));
    }
}
