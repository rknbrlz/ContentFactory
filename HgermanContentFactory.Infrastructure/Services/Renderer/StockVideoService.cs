using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace HgermanContentFactory.Infrastructure.Services.Renderer;

/// <summary>
/// Downloads royalty-free stock video clips from Pexels API.
/// Used as primary video source; falls back to AI images when no clip found.
/// Free tier: 200 req/hour — sufficient for batch production.
/// Get your API key at https://www.pexels.com/api/
/// </summary>
public class StockVideoService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<StockVideoService> _logger;

    private string ApiKey => _config["Pexels:ApiKey"]
        ?? throw new InvalidOperationException("Pexels:ApiKey not configured");

    private const string BaseUrl = "https://api.pexels.com/videos";

    // Niche → high-performing Pexels search keywords
    private static readonly Dictionary<string, List<string>> NicheKeywords = new()
    {
        ["Technology"]    = ["technology", "coding", "computer", "artificial intelligence", "digital"],
        ["Finance"]       = ["money", "investment", "stock market", "business finance", "banking"],
        ["Health"]        = ["fitness", "healthy lifestyle", "meditation", "workout", "wellness"],
        ["Gaming"]        = ["gaming", "video game", "esports", "controller", "gamer"],
        ["Education"]     = ["studying", "education", "learning", "classroom", "books"],
        ["Lifestyle"]     = ["lifestyle", "morning routine", "productivity", "daily life", "motivation"],
        ["Food"]          = ["cooking", "food", "restaurant", "chef", "recipe"],
        ["Travel"]        = ["travel", "adventure", "city", "nature landscape", "airplane"],
        ["Science"]       = ["science", "laboratory", "research", "experiment", "space"],
        ["Entertainment"] = ["entertainment", "music", "concert", "cinema", "performance"],
        ["Business"]      = ["business", "office", "meeting", "entrepreneur", "startup"],
        ["Sports"]        = ["sport", "athlete", "running", "gym", "competition"],
        ["Nature"]        = ["nature", "forest", "ocean", "mountain", "wildlife"],
        ["Fashion"]       = ["fashion", "style", "clothing", "model", "designer"],
        ["DIY"]           = ["diy", "crafting", "workshop", "tools", "handmade"],
    };

    public StockVideoService(HttpClient http, IConfiguration config,
        ILogger<StockVideoService> logger)
    {
        _http   = http;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Downloads <paramref name="count"/> short clips relevant to the niche.
    /// Returns local file paths. Empty list = use AI images as fallback.
    /// </summary>
    public async Task<List<string>> DownloadClipsAsync(
        string niche,
        string topic,
        string outputDir,
        int count = 5,
        int maxDurationSec = 15)
    {
        Directory.CreateDirectory(outputDir);
        var paths = new List<string>();

        // Build search terms: topic keywords first, then niche fallbacks
        var searchTerms = BuildSearchTerms(topic, niche);

        foreach (var term in searchTerms)
        {
            if (paths.Count >= count) break;

            var clips = await SearchClipsAsync(term, count - paths.Count, maxDurationSec);
            foreach (var clip in clips)
            {
                if (paths.Count >= count) break;

                var localPath = Path.Combine(outputDir, $"clip_{paths.Count:D2}.mp4");
                var ok = await DownloadFileAsync(clip.DownloadUrl, localPath);
                if (ok)
                {
                    paths.Add(localPath);
                    _logger.LogInformation("Downloaded stock clip {N}: {Term}", paths.Count, term);
                }
            }
        }

        _logger.LogInformation("Stock clips downloaded: {Count}/{Target} for niche {Niche}",
            paths.Count, count, niche);
        return paths;
    }

    /// <summary>
    /// Searches Pexels for video clips matching the query.
    /// Returns best-matching clips sorted by duration fit.
    /// </summary>
    private async Task<List<ClipResult>> SearchClipsAsync(
        string query,
        int count,
        int maxDurationSec)
    {
        try
        {
            _http.DefaultRequestHeaders.Clear();
            _http.DefaultRequestHeaders.Add("Authorization", ApiKey);

            var url      = $"{BaseUrl}/search?query={Uri.EscapeDataString(query)}" +
                           $"&per_page={count * 3}&orientation=portrait&size=medium";
            var response = await _http.GetFromJsonAsync<PexelsResponse>(url);

            if (response?.Videos == null) return [];

            return response.Videos
                .Where(v => v.Duration <= maxDurationSec && v.Duration >= 3)
                .SelectMany(v => v.VideoFiles
                    .Where(f => f.Quality == "hd" && f.FileType == "video/mp4"
                             && f.Width <= 1080)
                    .Select(f => new ClipResult(f.Link, v.Duration)))
                .OrderBy(c => Math.Abs(c.DurationSec - 10))  // prefer ~10s clips
                .Take(count)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Pexels search failed for query: {Query}", query);
            return [];
        }
    }

    private async Task<bool> DownloadFileAsync(string url, string localPath)
    {
        try
        {
            using var client   = new HttpClient();
            var bytes          = await client.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(localPath, bytes);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to download clip from {Url}", url);
            return false;
        }
    }

    private List<string> BuildSearchTerms(string topic, string niche)
    {
        // Extract keywords from topic title
        var topicWords = topic
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3)
            .Take(3)
            .ToList();

        var terms = new List<string>();

        // 1. Full topic first
        if (topic.Length > 3 && topic.Length < 50)
            terms.Add(topic);

        // 2. Topic keyword combinations
        if (topicWords.Count >= 2)
            terms.Add(string.Join(" ", topicWords.Take(2)));

        // 3. Niche-specific fallbacks
        var nicheTerms = NicheKeywords.GetValueOrDefault(niche, ["trending"]);
        terms.AddRange(nicheTerms.Take(3));

        return terms.Distinct().ToList();
    }

    // ── Response Models ────────────────────────────────────────────────────

    private record PexelsResponse(
        [property: JsonPropertyName("videos")] List<PexelsVideo>? Videos);

    private record PexelsVideo(
        [property: JsonPropertyName("duration")]    int Duration,
        [property: JsonPropertyName("video_files")] List<PexelsFile> VideoFiles);

    private record PexelsFile(
        [property: JsonPropertyName("link")]      string Link,
        [property: JsonPropertyName("quality")]   string Quality,
        [property: JsonPropertyName("file_type")] string FileType,
        [property: JsonPropertyName("width")]     int Width);

    private record ClipResult(string DownloadUrl, int DurationSec);
}
