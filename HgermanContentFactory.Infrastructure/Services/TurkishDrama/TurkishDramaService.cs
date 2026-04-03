using HgermanContentFactory.Core.DTOs;
using HgermanContentFactory.Core.Entities;
using HgermanContentFactory.Core.Enums;
using HgermanContentFactory.Core.Interfaces;
using HgermanContentFactory.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;

namespace HgermanContentFactory.Infrastructure.Services.TurkishDrama;

/// <summary>
/// Generates viral short-video content for Turkish drama channels in 6 languages.
/// Uses the Anthropic Claude API (claude-sonnet-4-20250514).
/// </summary>
public class TurkishDramaService : ITurkishDramaService
{
    private readonly HttpClient       _http;
    private readonly IConfiguration   _config;
    private readonly AppDbContext     _db;
    private readonly ILogger<TurkishDramaService> _logger;

    private string ApiKey => _config["Anthropic:ApiKey"]
        ?? throw new InvalidOperationException("Anthropic:ApiKey not configured.");

    private const string Model      = "claude-sonnet-4-20250514";
    private const string ApiUrl     = "https://api.anthropic.com/v1/messages";
    private const string ApiVersion = "2023-06-01";

    // ── Language metadata ──────────────────────────────────────────────────

    private static readonly Dictionary<ContentLanguage, LangMeta> Languages = new()
    {
        [ContentLanguage.Spanish] = new("Spanish (Español)", "es",
            "Latin America & Spain",
            "Audiences love passionate romance, family drama, and betrayal storylines. " +
            "Use telenovela-style emotional hooks. References to Turkish culture resonate well."),

        [ContentLanguage.French] = new("French (Français)", "fr",
            "France, Belgium, North Africa",
            "Viewers appreciate emotional depth and sophisticated storytelling. " +
            "Emphasize romantic tension and moral dilemmas. Avoid overly sensational language."),

        [ContentLanguage.German] = new("German (Deutsch)", "de",
            "Germany, Austria, Switzerland",
            "Focus on dramatic plot twists and character conflicts. " +
            "The audience prefers concise, high-impact scripts. Avoid excessive sentimentality."),

        [ContentLanguage.Italian] = new("Italian (Italiano)", "it",
            "Italy",
            "Italians connect deeply with family loyalty themes, honor, and forbidden love. " +
            "Use expressive, passionate language with cinematic flair."),

        [ContentLanguage.Polish]  = new("Polish (Polski)", "pl",
            "Poland",
            "Polish viewers enjoy slow-burn romance and misunderstandings between lovers. " +
            "Emphasize longing, sacrifice, and dramatic reunions."),

        [ContentLanguage.English] = new("English", "en",
            "UK, USA, Global",
            "Global English audience expects punchy hooks and fast-paced storytelling. " +
            "Reference the 'Turkish drama' phenomenon directly — many viewers are new fans."),
    };

    // ── Top Turkish dramas known to perform well per market ────────────────
    private static readonly List<string> PopularSeries = new()
    {
        "Kara Sevda (Endless Love)",
        "Aşk-ı Memnu (Forbidden Love)",
        "Diriliş Ertuğrul (Resurrection Ertugrul)",
        "Barbaroslar (Barbarossa)",
        "Sen Çal Kapımı (You Knock on My Door / Love Is in the Air)",
        "Yalı Çapkını (The Kingfisher)",
        "Fatih Harbiye",
        "Çukur (The Pit)",
        "Içerde (Inside)",
        "Ezel",
        "Muhtesem Yuzyil (Magnificent Century)",
        "Kurt Seyit ve Şura",
        "Yemin (The Promise)",
        "Zalim İstanbul (Ruthless Istanbul)",
        "Bir Zamanlar Çukurova (Once Upon a Time in Çukurova)"
    };

    public TurkishDramaService(HttpClient http, IConfiguration config,
        AppDbContext db, ILogger<TurkishDramaService> logger)
    {
        _http   = http;
        _config = config;
        _db     = db;
        _logger = logger;
    }

    // ── ITurkishDramaService ───────────────────────────────────────────────

    public async Task<List<TurkishDramaTrendDto>> GetTrendingDramasAsync(ContentLanguage language)
    {
        var meta = GetMeta(language);

        var prompt =
            $"You are a social media trend analyst specializing in Turkish TV dramas (dizi) " +
            $"for {meta.Name}-speaking markets ({meta.Market}).\n\n" +
            $"Market insight: {meta.Insight}\n\n" +
            $"Identify 8 currently viral Turkish drama moments or storylines that would make " +
            $"outstanding YouTube Shorts for {meta.Name} audiences right now.\n\n" +
            $"Consider: iconic scenes, plot twists, romantic moments, betrayals, " +
            $"character deaths, love triangles, and fan-favourite dialogues.\n\n" +
            $"Return a JSON array (no markdown, no explanation):\n" +
            $"[\n" +
            $"  {{\n" +
            $"    \"seriesName\": \"Series name (English title if available)\",\n" +
            $"    \"sceneOrEmotion\": \"Specific scene or emotional beat (max 10 words)\",\n" +
            $"    \"whyViral\": \"1 sentence: why this resonates with {meta.Name} viewers\",\n" +
            $"    \"trendScore\": 0-100,\n" +
            $"    \"keywords\": \"5 comma-separated {meta.Code} keywords\"\n" +
            $"  }}\n" +
            $"]";

        var raw = await CallClaudeAsync(prompt, maxTokens: 1800);

        try
        {
            var clean = raw.Replace("```json", "").Replace("```", "").Trim();
            var items = JsonSerializer.Deserialize<List<TrendRaw>>(clean,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            return items.Select(x => new TurkishDramaTrendDto
            {
                SeriesName     = x.SeriesName     ?? string.Empty,
                SceneOrEmotion = x.SceneOrEmotion ?? string.Empty,
                WhyViral       = x.WhyViral       ?? string.Empty,
                TrendScore     = x.TrendScore,
                Keywords       = x.Keywords       ?? string.Empty,
                Language       = language,
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse trending dramas JSON for {Lang}", language);
            return new();
        }
    }

    public async Task<TurkishDramaContentDto> GenerateContentAsync(TurkishDramaGenerateRequest req)
    {
        var meta = GetMeta(req.Language);

        var prompt = BuildContentPrompt(req.DramaSeries, req.SceneOrEmotion, meta);

        ContentRaw? parsed = null;
        try
        {
            var raw   = await CallClaudeAsync(prompt, maxTokens: 2000);
            var clean = raw.Replace("```json", "").Replace("```", "").Trim();
            parsed    = JsonSerializer.Deserialize<ContentRaw>(clean,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Content generation failed for {Series}/{Lang}",
                req.DramaSeries, req.Language);
            return new TurkishDramaContentDto
            {
                Language       = req.Language,
                DramaSeries    = req.DramaSeries,
                SceneOrEmotion = req.SceneOrEmotion,
                ErrorMessage   = ex.Message
            };
        }

        var dto = new TurkishDramaContentDto
        {
            Language       = req.Language,
            DramaSeries    = req.DramaSeries,
            SceneOrEmotion = req.SceneOrEmotion,
            Title          = parsed?.Title       ?? string.Empty,
            Script         = parsed?.Script      ?? string.Empty,
            Description    = parsed?.Description ?? string.Empty,
            Hashtags       = parsed?.Hashtags    ?? string.Empty,
            Hook           = parsed?.Hook        ?? string.Empty,
            CallToAction   = parsed?.CallToAction ?? string.Empty,
        };

        if (req.AutoSave && req.ChannelId.HasValue)
            dto.SavedVideoId = await SaveVideoAsync(dto, req.ChannelId.Value);

        return dto;
    }

    public async Task<List<TurkishDramaContentDto>> GenerateBulkAsync(
        string dramaSeries, string sceneOrEmotion)
    {
        var tasks = Languages.Keys.Select(lang =>
            GenerateContentAsync(new TurkishDramaGenerateRequest
            {
                DramaSeries    = dramaSeries,
                SceneOrEmotion = sceneOrEmotion,
                Language       = lang,
                AutoSave       = false,
            }));

        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static string BuildContentPrompt(string series, string scene, LangMeta meta) =>
        $"You are an expert short-video content creator for Turkish drama channels " +
        $"targeting {meta.Name}-speaking audiences ({meta.Market}).\n\n" +
        $"Turkish drama series : {series}\n" +
        $"Scene / emotion focus: {scene}\n" +
        $"Language             : Write ENTIRELY in {meta.Name}\n\n" +
        $"Audience insight: {meta.Insight}\n\n" +
        $"Generate a complete YouTube Shorts content package.\n\n" +
        $"Rules:\n" +
        $"- Script: 45-60 seconds (~130 words), all {meta.Name}, no Turkish words unless they are iconic show names\n" +
        $"- Hook must grab attention in the first 3 seconds\n" +
        $"- Tap into universal emotions: love, betrayal, sacrifice, longing\n" +
        $"- CTA must encourage comments or follows in {meta.Name}\n" +
        $"- Hashtags: 5 {meta.Name} drama hashtags + 5 niche + 5 platform + 3 Turkish drama specific\n\n" +
        $"Return ONLY this JSON (no markdown fences, no extra text):\n" +
        $"{{\n" +
        $"  \"title\": \"Viral YouTube Shorts title in {meta.Name} (max 70 chars, include emoji)\",\n" +
        $"  \"hook\": \"Opening sentence only — first 3 seconds\",\n" +
        $"  \"script\": \"Full narration script in {meta.Name}\",\n" +
        $"  \"callToAction\": \"Closing CTA sentence in {meta.Name}\",\n" +
        $"  \"description\": \"YouTube description in {meta.Name} (150-300 chars)\",\n" +
        $"  \"hashtags\": \"All hashtags space-separated starting with #\"\n" +
        $"}}";

    private async Task<int?> SaveVideoAsync(TurkishDramaContentDto dto, int channelId)
    {
        try
        {
            var video = new CF_Video
            {
                Title       = dto.Title,
                Description = dto.Description,
                Script      = dto.Script,
                Hashtags    = dto.Hashtags,
                Language    = dto.Language,
                Niche       = NicheCategory.TurkishDrama,
                ChannelId   = channelId,
                Status      = VideoStatus.ScriptReady,
                AIPromptUsed = $"TurkishDrama|{dto.DramaSeries}|{dto.SceneOrEmotion}",
            };
            _db.CF_Videos.Add(video);
            await _db.SaveChangesAsync();
            return video.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save TurkishDrama video to DB");
            return null;
        }
    }

    private async Task<string> CallClaudeAsync(string prompt, int maxTokens = 1500,
        int retryCount = 0)
    {
        _http.DefaultRequestHeaders.Clear();
        _http.DefaultRequestHeaders.Add("x-api-key", ApiKey);
        _http.DefaultRequestHeaders.Add("anthropic-version", ApiVersion);

        var body = new
        {
            model      = Model,
            max_tokens = maxTokens,
            messages   = new[] { new { role = "user", content = prompt } }
        };

        var resp = await _http.PostAsJsonAsync(ApiUrl, body);

        if (resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests && retryCount < 3)
        {
            var wait = (retryCount + 1) * 15;
            _logger.LogWarning("Claude rate limit — retrying in {Sec}s ({N}/3)", wait, retryCount + 1);
            await Task.Delay(TimeSpan.FromSeconds(wait));
            return await CallClaudeAsync(prompt, maxTokens, retryCount + 1);
        }

        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<ClaudeResponse>();
        return result?.Content?.FirstOrDefault(c => c.Type == "text")?.Text?.Trim()
               ?? string.Empty;
    }

    private static LangMeta GetMeta(ContentLanguage lang) =>
        Languages.TryGetValue(lang, out var m) ? m
        : new LangMeta("English", "en", "Global", "Write for a general international audience.");

    // ── Internal models ────────────────────────────────────────────────────

    private record LangMeta(string Name, string Code, string Market, string Insight);

    private record TrendRaw(
        string? SeriesName,
        string? SceneOrEmotion,
        string? WhyViral,
        double  TrendScore,
        string? Keywords);

    private record ContentRaw(
        string? Title,
        string? Hook,
        string? Script,
        string? CallToAction,
        string? Description,
        string? Hashtags);

    private record ClaudeResponse(ClaudeContent[]? Content);
    private record ClaudeContent(string Type, string? Text);
}
