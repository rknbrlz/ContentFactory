using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;

namespace HgermanContentFactory.Infrastructure.Services.Renderer;

/// <summary>
/// Converts script text to MP3 audio using ElevenLabs API.
/// Supports all 6 content languages with matching voice IDs.
/// </summary>
public class ElevenLabsService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<ElevenLabsService> _logger;

    private string ApiKey => _config["ElevenLabs:ApiKey"]
        ?? throw new InvalidOperationException("ElevenLabs:ApiKey not configured");

    private string BaseUrl => "https://api.elevenlabs.io/v1";

    // ── Default voice IDs per language ────────────────────────────────────
    // Replace these with your own cloned/preferred voices from ElevenLabs dashboard
    private static readonly Dictionary<string, string> DefaultVoices = new()
    {
        ["English"] = "21m00Tcm4TlvDq8ikWAM",   // Rachel  — neutral English
        ["German"]  = "AZnzlk1XvdvUeBnXmlld",   // Domi    — German accent
        ["Spanish"] = "EXAVITQu4vr4xnSDxMaL",   // Bella   — neutral Spanish
        ["French"]  = "MF3mGyEYCl7XYWbV9V6O",   // Elli    — French accent
        ["Italian"] = "TxGEqnHWrfWFTfGW9XjX",   // Josh    — Italian accent
        ["Polish"]  = "pNInz6obpgDQGcFmaJgB",   // Adam    — Polish accent
    };

    public ElevenLabsService(HttpClient http, IConfiguration config,
        ILogger<ElevenLabsService> logger)
    {
        _http   = http;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Converts <paramref name="script"/> to an MP3 file at <paramref name="outputPath"/>.
    /// </summary>
    public async Task<bool> TextToSpeechAsync(
        string script,
        string language,
        string outputPath,
        string? voiceId = null)
    {
        try
        {
            // Pick voice — config override → per-language default → fallback English
            var voice = voiceId
                ?? _config[$"ElevenLabs:Voices:{language}"]
                ?? DefaultVoices.GetValueOrDefault(language)
                ?? DefaultVoices["English"];

            _http.DefaultRequestHeaders.Clear();
            _http.DefaultRequestHeaders.Add("xi-api-key", ApiKey);
            _http.DefaultRequestHeaders.Add("Accept", "audio/mpeg");

            var body = new
            {
                text = script,
                model_id = "eleven_multilingual_v2",   // supports all 6 languages
                voice_settings = new
                {
                    stability        = 0.5,
                    similarity_boost = 0.85,
                    style            = 0.3,
                    use_speaker_boost= true
                }
            };

            var response = await _http.PostAsJsonAsync(
                $"{BaseUrl}/text-to-speech/{voice}", body);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                _logger.LogError("ElevenLabs error {Status}: {Body}",
                    response.StatusCode, err);
                return false;
            }

            var audioBytes = await response.Content.ReadAsByteArrayAsync();
            await File.WriteAllBytesAsync(outputPath, audioBytes);

            _logger.LogInformation("TTS audio written: {Path} ({Bytes} bytes)",
                outputPath, audioBytes.Length);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ElevenLabs TTS failed for language {Lang}", language);
            return false;
        }
    }

    /// <summary>Returns available ElevenLabs voices for the account.</summary>
    public async Task<List<VoiceInfo>> GetVoicesAsync()
    {
        _http.DefaultRequestHeaders.Clear();
        _http.DefaultRequestHeaders.Add("xi-api-key", ApiKey);

        var resp = await _http.GetFromJsonAsync<VoicesResponse>($"{BaseUrl}/voices");
        return resp?.Voices ?? [];
    }

    // ── Inner types ────────────────────────────────────────────────────────

    public record VoiceInfo(
        string Voice_Id,
        string Name,
        string? Category);

    private record VoicesResponse(List<VoiceInfo> Voices);
}
