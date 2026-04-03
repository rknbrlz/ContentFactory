using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace HgermanContentFactory.Infrastructure.Services.Renderer;

/// <summary>
/// Generates scene images for video frames using OpenAI DALL-E 3.
/// Each image covers ~10 seconds of the short video.
/// </summary>
public class ImageGenerationService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<ImageGenerationService> _logger;

    private string ApiKey => _config["OpenAI:ApiKey"]
        ?? throw new InvalidOperationException("OpenAI:ApiKey not configured");

    public ImageGenerationService(HttpClient http, IConfiguration config,
        ILogger<ImageGenerationService> logger)
    {
        _http   = http;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Generates <paramref name="count"/> scene images for a short video.
    /// Returns list of local file paths.
    /// </summary>
    public async Task<List<string>> GenerateSceneImagesAsync(
        string title,
        string script,
        string niche,
        string outputDir,
        int count = 5)
    {
        Directory.CreateDirectory(outputDir);

        // Split script into scenes
        var scenes = SplitIntoScenes(script, count);
        var paths  = new List<string>();

        for (int i = 0; i < scenes.Count; i++)
        {
            var prompt = BuildImagePrompt(title, scenes[i], niche, i == 0);
            var path   = Path.Combine(outputDir, $"scene_{i:D2}.png");

            var ok = await GenerateImageAsync(prompt, path);
            if (ok)
                paths.Add(path);
            else
                _logger.LogWarning("Scene {Index} image failed, skipping", i);

            // Rate limit: DALL-E 3 allows 5 images/min on tier-1
            if (i < scenes.Count - 1)
                await Task.Delay(TimeSpan.FromSeconds(13));
        }

        _logger.LogInformation("Generated {Count} scene images in {Dir}", paths.Count, outputDir);
        return paths;
    }

    /// <summary>Generates a thumbnail image (1792x1024 landscape).</summary>
    public async Task<string?> GenerateThumbnailAsync(
        string title,
        string niche,
        string outputPath)
    {
        var prompt = $"Eye-catching YouTube Shorts thumbnail for '{title}'. " +
                     $"Niche: {niche}. Bold text overlay space. Vibrant colors. " +
                     $"Professional, high-contrast, attention-grabbing. No watermarks.";

        var ok = await GenerateImageAsync(prompt, outputPath, size: "1792x1024");
        return ok ? outputPath : null;
    }

    // ── Private ────────────────────────────────────────────────────────────

    private async Task<bool> GenerateImageAsync(string prompt, string outputPath,
        string size = "1024x1792")   // portrait for Shorts
    {
        try
        {
            _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiKey);

            var body = new
            {
                model   = "dall-e-3",
                prompt,
                n       = 1,
                size,
                quality = "standard",
                response_format = "url"
            };

            var resp = await _http.PostAsJsonAsync(
                "https://api.openai.com/v1/images/generations", body);

            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync();
                _logger.LogError("DALL-E error {Status}: {Body}", resp.StatusCode, err);
                return false;
            }

            var result = await resp.Content.ReadFromJsonAsync<DalleResponse>();
            var url    = result?.Data?.FirstOrDefault()?.Url;
            if (string.IsNullOrEmpty(url)) return false;

            // Download the image
            using var img = new HttpClient();
            var bytes = await img.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(outputPath, bytes);

            _logger.LogDebug("Image saved: {Path}", outputPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Image generation failed for {Path}", outputPath);
            return false;
        }
    }

    private static List<string> SplitIntoScenes(string script, int count)
    {
        // Split by sentence, group into roughly equal chunks
        var sentences = script
            .Split(new[] { ". ", "! ", "? ", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Where(s => s.Trim().Length > 5)
            .ToList();

        if (sentences.Count == 0) return Enumerable.Repeat(script, count).ToList();

        var scenes    = new List<string>();
        int chunkSize = Math.Max(1, (int)Math.Ceiling(sentences.Count / (double)count));

        for (int i = 0; i < count && i * chunkSize < sentences.Count; i++)
        {
            var chunk = sentences.Skip(i * chunkSize).Take(chunkSize);
            scenes.Add(string.Join(". ", chunk));
        }

        // Pad to exact count if needed
        while (scenes.Count < count)
            scenes.Add(scenes.Last());

        return scenes;
    }

    private static string BuildImagePrompt(string title, string sceneText,
        string niche, bool isHook)
    {
        var styleMap = new Dictionary<string, string>
        {
            ["Technology"]    = "futuristic digital interface, neon glow, dark background",
            ["Finance"]       = "professional financial charts, gold and dark blue palette",
            ["Health"]        = "clean medical aesthetic, bright whites, wellness vibes",
            ["Gaming"]        = "vibrant gaming setup, RGB lighting, dramatic angles",
            ["Education"]     = "clean infographic style, whiteboard, learning aesthetic",
            ["Lifestyle"]     = "bright lifestyle photography, warm tones, aspirational",
            ["Food"]          = "close-up food photography, rich colors, appetizing",
            ["Travel"]        = "breathtaking landscape, golden hour, wanderlust",
            ["Science"]       = "scientific visualization, molecular structures, cosmos",
            ["Entertainment"] = "cinematic, dramatic lighting, vibrant colors",
            ["Business"]      = "corporate modern, clean lines, confident aesthetic",
            ["Sports"]        = "dynamic action, motion blur, energy and power",
            ["Nature"]        = "stunning nature photography, vivid colors, serene",
            ["Fashion"]       = "editorial fashion, studio lighting, aesthetic",
            ["DIY"]           = "hands-on crafting, warm workshop lighting, creative",
        };

        var style = styleMap.GetValueOrDefault(niche, "cinematic, professional, eye-catching");
        var hook  = isHook ? "Bold, shocking, immediately attention-grabbing. " : "";

        return $"{hook}Cinematic illustration for a short video. " +
               $"Topic: {title}. Scene: {sceneText.Trim()[..Math.Min(100, sceneText.Length)]}. " +
               $"Visual style: {style}. " +
               $"9:16 portrait format for mobile. No text, no watermarks. High quality.";
    }

    // ── Response types ─────────────────────────────────────────────────────

    private record DalleResponse(List<DalleImage>? Data);
    private record DalleImage(string? Url);
}
