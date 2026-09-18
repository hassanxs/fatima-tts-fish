using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FatimaTTS.Services;

/// <summary>
/// Checks GitHub Releases API for newer versions of Fatima TTS (Fish).
/// Repository: https://github.com/hassanxs/fatima-tts-fish
/// </summary>
public class GitHubUpdateService
{
    // ── Config ──────────────────────────────────────────────────────────
    public const string GitHubOwner = "hassanxs";
    public const string GitHubRepo  = "fatima-tts-fish";

    // Read from the running assembly rather than hand-maintained — a
    // hardcoded literal here silently drifts from the real app version
    // (it did once already: stuck at 1.0.0 through the 1.0.1 release).
    public static readonly string CurrentVersion = ReadAssemblyVersion();

    public static readonly string ReleasesUrl =
        $"https://github.com/{GitHubOwner}/{GitHubRepo}/releases";
    public static readonly string LatestApiUrl =
        $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
    // ─────────────────────────────────────────────────────────────────────

    private readonly HttpClient  _http;
    private readonly AppLogger   _log;

    public GitHubUpdateService(AppLogger log)
    {
        _log  = log;
        _http = new HttpClient();
        // GitHub API requires a User-Agent header
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"FatimaTTSFish/{CurrentVersion}");
        // Long enough to cover downloading the ~55MB installer, not just the API check.
        _http.Timeout = TimeSpan.FromMinutes(5);
    }

    private static string ReadAssemblyVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    /// <summary>
    /// Checks GitHub for a newer release.
    /// Returns null if already up to date or check fails.
    /// </summary>
    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            _log.Info($"Checking for updates (current: v{CurrentVersion})");

            var json     = await _http.GetStringAsync(LatestApiUrl, ct);
            var release  = JsonSerializer.Deserialize<GitHubRelease>(json);

            if (release is null || string.IsNullOrWhiteSpace(release.TagName))
            {
                _log.Warn("No release found on GitHub");
                return null;
            }

            var latestVersion = release.TagName.TrimStart('v');
            _log.Info($"Latest GitHub release: v{latestVersion}");

            if (!IsNewer(latestVersion, CurrentVersion))
                return null;

            // The MSI installer is the only distributed asset (no portable exe since v1.0.1)
            var msiAsset = release.Assets?.FirstOrDefault(a =>
                a.Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase));

            return new UpdateInfo
            {
                Version           = latestVersion,
                ReleaseNotes      = release.Body ?? "",
                PublishedAt       = release.PublishedAt,
                DownloadUrl       = msiAsset?.BrowserDownloadUrl ?? ReleasesUrl,
                ReleasesUrl       = ReleasesUrl,
                IsInstallerAvailable = msiAsset is not null
            };
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _log.Error("Update check failed", ex);
            return null;
        }
    }

    /// <summary>
    /// Downloads the update's MSI installer to a temp file, reporting 0–100
    /// progress. Caller is responsible for launching it and for cleanup.
    /// </summary>
    public async Task<string> DownloadInstallerAsync(
        string downloadUrl, IProgress<int>? progress, CancellationToken ct = default)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"FatimaTTSFish-update-{Guid.NewGuid():N}.msi");

        using var response = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(
            tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        var buffer     = new byte[81920];
        long totalRead = 0;
        int  read;
        while ((read = await contentStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
            totalRead += read;
            if (totalBytes > 0)
                progress?.Report((int)(totalRead * 100 / totalBytes));
        }

        _log.Info($"Downloaded update installer to {tempPath} ({totalRead:N0} bytes)");
        return tempPath;
    }

    /// <summary>
    /// Returns true if <paramref name="latest"/> is a higher version than <paramref name="current"/>.
    /// Compares as semantic version (major.minor.patch).
    /// </summary>
    private static bool IsNewer(string latest, string current)
    {
        if (Version.TryParse(latest,  out var lv) &&
            Version.TryParse(current, out var cv))
            return lv > cv;

        // Fallback: string comparison
        return string.Compare(latest, current, StringComparison.Ordinal) > 0;
    }
}

// ── DTOs ─────────────────────────────────────────────────────────────────

public record UpdateInfo
{
    public string   Version       { get; init; } = "";
    public string   ReleaseNotes  { get; init; } = "";
    public DateTime PublishedAt   { get; init; }
    public string   DownloadUrl   { get; init; } = "";
    public string   ReleasesUrl   { get; init; } = "";
    public bool     IsInstallerAvailable { get; init; }
}

file class GitHubRelease
{
    [JsonPropertyName("tag_name")]    public string?  TagName     { get; set; }
    [JsonPropertyName("body")]        public string?  Body        { get; set; }
    [JsonPropertyName("published_at")] public DateTime PublishedAt { get; set; }
    [JsonPropertyName("assets")]      public List<GitHubAsset>? Assets { get; set; }
}

file class GitHubAsset
{
    [JsonPropertyName("name")]                 public string Name                { get; set; } = "";
    [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; } = "";
}
