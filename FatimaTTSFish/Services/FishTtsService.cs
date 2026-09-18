using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FatimaTTS.Models;

namespace FatimaTTS.Services;

/// <summary>
/// Thin HTTP client over the Fish.audio TTS API (https://api.fish.audio).
///
/// Genuinely different from a typical REST TTS call in two ways:
///   - Auth is Bearer (not Basic), and the model id is sent as an HTTP HEADER,
///     not a body field.
///   - A successful response is STREAMING CHUNKED BINARY AUDIO — there is no
///     JSON envelope on success (unlike Inworld's audioContent/base64 shape).
///     We buffer the whole stream into a byte[] since the rest of the app
///     (AudioMergeService, chunk persistence) works with complete byte arrays.
///
/// Built against Fish's documented request/response shapes (docs.fish.audio,
/// Sept 2026) and confirmed working end-to-end (synthesis + voice listing)
/// against a live account.
/// </summary>
public class FishTtsService
{
    private const string BaseUrl = "https://api.fish.audio";
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly AppLogger  _log;

    public FishTtsService(AppLogger log)
    {
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _log  = log;
    }

    /// <summary>
    /// Synthesizes a single text chunk. Returns the raw audio bytes and the
    /// number of UTF-8 bytes billed (Fish bills per UTF-8 byte, not per
    /// character). Fish's response carries no confirmed usage/byte-count
    /// field, so the billed amount is computed client-side from the request
    /// text — an estimate, not an API-reported figure, until verified.
    /// </summary>
    public async Task<(byte[] AudioBytes, long ProcessedBytes)> SynthesizeAsync(
        string apiKey,
        string text,
        string referenceId,
        string modelId,
        string audioEncoding,
        double temperature,
        double topP,
        double speed,
        bool normalize,
        CancellationToken ct = default)
    {
        var fishFormat = MapFormat(audioEncoding);
        var payload = new FishSynthesizeRequest
        {
            Text        = text,
            ReferenceId = referenceId,
            Temperature = temperature,
            TopP        = topP,
            Format      = fishFormat,
            // Fixed at 44100 for WAV/PCM so our merge/duration math (AudioMergeService)
            // has a known value rather than relying on Fish's per-format default.
            SampleRate  = fishFormat is "wav" or "pcm" ? 44100 : null,
            Normalize   = normalize,
            Prosody     = new FishProsody { Speed = speed }
        };

        var json    = JsonSerializer.Serialize(payload, JsonOpts);
        var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/v1/tts");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Add("model", modelId);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw BuildApiException(response, body);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        var audioBytes = buffer.ToArray();

        long processedBytes = Encoding.UTF8.GetByteCount(text);
        return (audioBytes, processedBytes);
    }

    /// <summary>
    /// Fish has no documented dedicated key-validation endpoint, so this sends
    /// a real synthesis request with a deliberately-invalid reference id.
    /// A 401/403 means the key itself was rejected; any other outcome (success,
    /// or a non-auth error like "reference id not found") means the key was
    /// accepted before that failure occurred — the standard auth-then-validate
    /// request order. Unverified against a live account.
    /// </summary>
    public async Task<bool> ValidateApiKeyAsync(string apiKey, CancellationToken ct = default)
    {
        try
        {
            await SynthesizeAsync(
                apiKey, "Hi", "fatima-tts-fish-key-validation-probe", "s1", "MP3",
                temperature: 0.7, topP: 0.7, speed: 1.0, normalize: false, ct);
            return true;
        }
        catch (FishApiException ex)
        {
            return !ex.IsAuthError;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Lists voice models — the caller's own (<paramref name="selfOnly"/> = true)
    /// or Fish's public Voice Library (<paramref name="selfOnly"/> = false, the
    /// same catalog voices shown on fish.audio). Confirmed live endpoint —
    /// see docs.fish.audio "Manage Voices". Returns at most <paramref
    /// name="pageSize"/> voices from a single page; callers that need more
    /// should page with <paramref name="pageNumber"/>, and can narrow the
    /// public library with <paramref name="title"/> (a substring search —
    /// the library is far too large to list in full). Each returned voice's
    /// Id is the reference_id accepted by SynthesizeAsync.
    /// </summary>
    public async Task<FishVoiceListResponse> ListVoicesAsync(
        string apiKey,
        bool selfOnly = true,
        int pageSize = 100,
        int pageNumber = 1,
        string? title = null,
        CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/model?self={(selfOnly ? "true" : "false")}&page_size={pageSize}&page_number={pageNumber}";
        if (!string.IsNullOrWhiteSpace(title))
            url += $"&title={Uri.EscapeDataString(title)}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var response = await _http.SendAsync(request, ct);
        var body     = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw BuildApiException(response, body);

        return ParseVoiceListResponse(body);
    }

    /// <summary>
    /// Parses the /model listing body defensively. The full API reference
    /// (as opposed to the simplified guide-page example) confirms the item's
    /// id field is "_id", so that's tried first, with a few plausible
    /// alternates as fallback in case the live shape ever differs — rather
    /// than silently returning voices with an empty Id (which would look
    /// selectable in the UI but fail to actually fill in a usable reference_id).
    /// </summary>
    private FishVoiceListResponse ParseVoiceListResponse(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var result = new FishVoiceListResponse
        {
            Total   = root.TryGetProperty("total", out var t) && t.TryGetInt32(out var tv) ? tv : 0,
            HasMore = root.TryGetProperty("has_more", out var hm) && hm.ValueKind is JsonValueKind.True,
        };

        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            _log.Info($"Fish voice listing: unexpected response shape, no 'items' array. Raw body: {Truncate(body, 500)}");
            return result;
        }

        foreach (var item in items.EnumerateArray())
        {
            var id    = FirstStringProperty(item, "_id", "id", "model_id", "reference_id", "voice_id");
            var title = FirstStringProperty(item, "title", "name") ?? "";
            var state = FirstStringProperty(item, "state", "status");
            var vis   = FirstStringProperty(item, "visibility");

            if (string.IsNullOrEmpty(id))
                _log.Info($"Fish voice listing: item has no recognizable id field. Raw item: {Truncate(item.GetRawText(), 300)}");

            result.Items.Add(new FishVoice { Id = id ?? "", Title = title, State = state, Visibility = vis });
        }

        return result;
    }

    /// <summary>
    /// Clones a persistent voice model from one or more audio samples
    /// (POST /model, multipart/form-data — confirmed live endpoint, see
    /// docs.fish.audio "Voice Cloning"). With the default train_mode="fast"
    /// the returned voice is usable as a reference_id almost immediately;
    /// its State should read "trained" (occasionally "created"/"training"
    /// briefly). <paramref name="texts"/>, if supplied, must line up 1:1
    /// with <paramref name="audioFilePaths"/> and sharpens pronunciation —
    /// omit it to let Fish run ASR on the samples instead.
    /// </summary>
    public async Task<FishVoice> CloneVoiceAsync(
        string apiKey,
        string title,
        IReadOnlyList<string> audioFilePaths,
        string? description = null,
        string visibility = "private",
        IReadOnlyList<string>? texts = null,
        bool enhanceAudioQuality = true,
        CancellationToken ct = default)
    {
        if (audioFilePaths.Count == 0)
            throw new ArgumentException("At least one audio sample is required.", nameof(audioFilePaths));

        using var content = new MultipartFormDataContent
        {
            { new StringContent("tts"), "type" },
            { new StringContent(title), "title" },
            { new StringContent("fast"), "train_mode" },
            { new StringContent(visibility), "visibility" },
            { new StringContent(enhanceAudioQuality ? "true" : "false"), "enhance_audio_quality" },
        };

        if (!string.IsNullOrWhiteSpace(description))
            content.Add(new StringContent(description), "description");

        foreach (var path in audioFilePaths)
        {
            var bytes       = await File.ReadAllBytesAsync(path, ct);
            var fileContent = new ByteArrayContent(bytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(GuessAudioMimeType(path));
            content.Add(fileContent, "voices", Path.GetFileName(path));
        }

        if (texts is not null)
            foreach (var text in texts)
                content.Add(new StringContent(text), "texts");

        var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/model") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var response = await _http.SendAsync(request, ct);
        var body      = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw BuildApiException(response, body);

        using var doc = JsonDocument.Parse(body);
        var root  = doc.RootElement;
        var id    = FirstStringProperty(root, "_id", "id", "model_id");
        var state = FirstStringProperty(root, "state");

        if (string.IsNullOrEmpty(id))
            _log.Info($"Fish voice clone: response has no recognizable id field. Raw body: {Truncate(body, 500)}");

        return new FishVoice
        {
            Id         = id ?? "",
            Title      = FirstStringProperty(root, "title") ?? title,
            State      = state,
            Visibility = FirstStringProperty(root, "visibility") ?? visibility,
        };
    }

    /// <summary>
    /// Renames, re-tags, or changes visibility on an existing voice model
    /// (PATCH /model/{id} — confirmed live endpoint, see docs.fish.audio
    /// "Manage Voices"). Only the fields passed change; omit a parameter to
    /// leave it as-is. <paramref name="visibility"/> "public" is silently
    /// downgraded to "private" by Fish's own API — same caveat as CloneVoiceAsync.
    /// </summary>
    public async Task<FishVoice> UpdateVoiceAsync(
        string apiKey,
        string voiceId,
        string? title = null,
        string? description = null,
        string? visibility = null,
        CancellationToken ct = default)
    {
        var fields = new Dictionary<string, object>();
        if (title is not null)       fields["title"]       = title;
        if (description is not null) fields["description"] = description;
        if (visibility is not null)  fields["visibility"]  = visibility;

        var json    = JsonSerializer.Serialize(fields, JsonOpts);
        var request = new HttpRequestMessage(HttpMethod.Patch, $"{BaseUrl}/model/{Uri.EscapeDataString(voiceId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http.SendAsync(request, ct);
        var body     = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw BuildApiException(response, body);

        // Some successful PATCH responses come back with an empty body (e.g. 204
        // No Content) — fall back to what we already know we asked for rather
        // than trying to parse zero bytes as JSON.
        if (string.IsNullOrWhiteSpace(body))
            return new FishVoice { Id = voiceId, Title = title ?? "", Visibility = visibility };

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        return new FishVoice
        {
            Id         = FirstStringProperty(root, "_id", "id") ?? voiceId,
            Title      = FirstStringProperty(root, "title") ?? title ?? "",
            State      = FirstStringProperty(root, "state"),
            Visibility = FirstStringProperty(root, "visibility") ?? visibility,
        };
    }

    /// <summary>
    /// Permanently deletes a voice model (DELETE /model/{id} — confirmed
    /// live endpoint, see docs.fish.audio "Manage Voices"). Irreversible.
    /// </summary>
    public async Task DeleteVoiceAsync(string apiKey, string voiceId, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"{BaseUrl}/model/{Uri.EscapeDataString(voiceId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw BuildApiException(response, body);
        }
    }

    private static FishApiException BuildApiException(HttpResponseMessage response, string body)
    {
        var status = (int)response.StatusCode;
        try
        {
            var err = JsonSerializer.Deserialize<FishApiError>(body, JsonOpts);
            return new FishApiException(
                FishApiException.FriendlyMessage(status, err?.Message ?? err?.Error ?? err?.Detail ?? body), status);
        }
        catch
        {
            return new FishApiException(FishApiException.FriendlyMessage(status, body), status);
        }
    }

    private static string GuessAudioMimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".wav"  => "audio/wav",
        ".mp3"  => "audio/mpeg",
        ".m4a"  => "audio/mp4",
        ".opus" => "audio/opus",
        _       => "application/octet-stream"
    };

    private static string? FirstStringProperty(JsonElement element, params string[] names)
    {
        foreach (var name in names)
            if (element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString();
        return null;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private static string MapFormat(string audioEncoding) => audioEncoding.ToUpperInvariant() switch
    {
        "MP3"  => "mp3",
        "WAV"  => "wav",
        "PCM"  => "pcm",
        "OPUS" => "opus",
        _      => "mp3"
    };
}
