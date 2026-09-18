using System.Text;
using System.Text.Json.Serialization;

namespace FatimaTTS.Models;

public class AppSettings
{
    public const string DefaultModel = "s2.1-pro";

    public string Theme { get; set; } = "Dark";
    public string DefaultReferenceId { get; set; } = "";          // Fish "reference_id" — no catalog to pick from
    public string DefaultModelId { get; set; } = DefaultModel;
    public string DefaultAudioEncoding { get; set; } = "MP3";
    public double DefaultTemperature { get; set; } = 0.7;
    public double DefaultTopP { get; set; } = 0.7;
    public double DefaultSpeakingRate { get; set; } = 1.0;        // maps to Fish's prosody.speed
    public bool DefaultApplyTextNormalization { get; set; } = true;
    public string OutputFolder { get; set; } = string.Empty;
    public bool AutoPlay { get; set; } = true;
    public bool SaveChunksOnComplete { get; set; } = false;
    public int MaxParallelChunks { get; set; } = 3;               // concurrent chunk syntheses

    // Editable per-model price per 1,000,000 UTF-8 bytes (USD). Seeded with Fish's published rates.
    public Dictionary<string, double> PricePerMillionBytes { get; set; } = new(DefaultPricePerMillion);

    [JsonIgnore]
    public static readonly string[] AvailableModels =
    [
        "s2.1-pro",
        "s2-pro",
        "s1",
        "s2.1-pro-free",
        "drama-3-preview",
    ];

    [JsonIgnore]
    public static readonly Dictionary<string, string> ModelDisplayNames = new()
    {
        ["s2.1-pro"]        = "Fish S2.1 Pro",
        ["s2-pro"]          = "Fish S2 Pro",
        ["s1"]              = "Fish S1",
        ["s2.1-pro-free"]   = "Fish S2.1 Pro (Free tier)",
        ["drama-3-preview"] = "Fish Drama 3 (Preview)",
    };

    [JsonIgnore]
    public static readonly Dictionary<string, string> ModelDescriptions = new()
    {
        ["s2.1-pro"]        = "Current flagship — best overall quality",
        ["s2-pro"]          = "Previous-generation pro model",
        ["s1"]              = "Original model",
        ["s2.1-pro-free"]   = "Free tier for development/testing — fair-use limits apply",
        ["drama-3-preview"] = "Preview model — pricing/behavior may change",
    };

    public static string NormalizeModelId(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            return DefaultModel;
        return ModelDisplayNames.ContainsKey(modelId) ? modelId : DefaultModel;
    }

    [JsonIgnore]
    public static readonly Dictionary<string, string> AudioEncodings = new()
    {
        ["MP3"]  = "MP3",
        ["WAV"]  = "WAV (PCM 16-bit)",
        ["PCM"]  = "PCM (raw, no container)",
        ["OPUS"] = "Opus",
    };

    [JsonIgnore]
    public static readonly Dictionary<string, string> AudioExtensions = new()
    {
        ["MP3"]  = "mp3",
        ["WAV"]  = "wav",
        ["PCM"]  = "pcm",
        ["OPUS"] = "opus",
    };

    // ── Pricing (USD per 1,000,000 UTF-8 bytes) ─────────────────────────────
    // Fish bills per UTF-8 byte, not per character (verify at https://fish.audio/pricing).
    // drama-3-preview's rate is not published in the docs reviewed — defaulted to the
    // standard $15/1M rate as a placeholder; confirm before relying on it for billing.
    [JsonIgnore]
    public static readonly Dictionary<string, double> DefaultPricePerMillion = new()
    {
        ["s2.1-pro"]        = 15.0,
        ["s2-pro"]          = 15.0,
        ["s1"]              = 15.0,
        ["s2.1-pro-free"]   = 0.0,
        ["drama-3-preview"] = 15.0,
    };

    /// <summary>Price per 1M UTF-8 bytes for a model, falling back to seeded defaults for unknown/absent keys.</summary>
    public double GetPricePerMillion(string modelId)
    {
        var id = NormalizeModelId(modelId);
        if (PricePerMillionBytes.TryGetValue(id, out var p)) return p;
        return DefaultPricePerMillion.GetValueOrDefault(id, 0.0);
    }

    /// <summary>Estimated USD cost for synthesizing the given text with a model — billed by UTF-8 byte count.</summary>
    public double EstimateCost(string text, string modelId)
        => Encoding.UTF8.GetByteCount(text) / 1_000_000.0 * GetPricePerMillion(modelId);
}
