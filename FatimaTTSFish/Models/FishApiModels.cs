using System.Text.Json.Serialization;

namespace FatimaTTS.Models;

// ── Synthesize (POST /v1/tts) ───────────────────────────────────────────────
// Fish's response on success is raw streaming binary audio (Transfer-Encoding:
// chunked) — there is no JSON success envelope, unlike Inworld. `model` is sent
// as an HTTP header, not a body field, so it has no property here.

public class FishSynthesizeRequest
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("reference_id")]
    public string ReferenceId { get; set; } = string.Empty;

    [JsonPropertyName("temperature")]
    public double Temperature { get; set; } = 0.7;

    [JsonPropertyName("top_p")]
    public double TopP { get; set; } = 0.7;

    [JsonPropertyName("format")]
    public string Format { get; set; } = "mp3";

    [JsonPropertyName("mp3_bitrate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int? Mp3Bitrate { get; set; }

    [JsonPropertyName("sample_rate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SampleRate { get; set; }

    [JsonPropertyName("normalize")]
    public bool Normalize { get; set; } = true;

    [JsonPropertyName("latency")]
    public string Latency { get; set; } = "normal";

    [JsonPropertyName("prosody")]
    public FishProsody? Prosody { get; set; }
}

public class FishProsody
{
    [JsonPropertyName("speed")]
    public double Speed { get; set; } = 1.0;

    [JsonPropertyName("volume")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Volume { get; set; }
}

// ── Voice listing (GET /model) ─────────────────────────────────────────────
// Confirmed live at docs.fish.audio (Manage Voices — Sept 2026):
//   GET /model?self=true&page_size=20&page_number=1
//   Authorization: Bearer {token}
// Returns { total, items: [...], has_more }. Each item's `id` is the
// `reference_id` value used by POST /v1/tts.

public class FishVoiceListResponse
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("items")]
    public List<FishVoice> Items { get; set; } = [];

    [JsonPropertyName("has_more")]
    public bool HasMore { get; set; }
}

public class FishVoice
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("visibility")]
    public string? Visibility { get; set; }
}

// ── Error body ───────────────────────────────────────────────────────────────
// Fish's error response schema is not documented (only the success path is).
// This is a defensive best-guess shape covering common REST error field names;
// FishTtsService falls back to the raw response body when none of these parse.
public class FishApiError
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }
}
