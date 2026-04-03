using HgermanContentFactory.Core.DTOs;
using HgermanContentFactory.Core.Enums;
using HgermanContentFactory.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;

namespace HgermanContentFactory.Infrastructure.Services.AI;

public class ContentGenerationService : IContentGenerationService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<ContentGenerationService> _logger;

    private string ApiKey => _config["OpenAI:ApiKey"]
        ?? throw new InvalidOperationException("OpenAI:ApiKey not set");
    private string Model => _config["OpenAI:Model"] ?? "gpt-4o";

    private static readonly Dictionary<ContentLanguage, string> LangNames = new()
    {
        [ContentLanguage.English] = "English",
        [ContentLanguage.German] = "German (Deutsch)",
        [ContentLanguage.Spanish] = "Spanish (Español)",
        [ContentLanguage.French] = "French (Français)",
        [ContentLanguage.Italian] = "Italian (Italiano)",
        [ContentLanguage.Polish] = "Polish (Polski)"
    };

    public ContentGenerationService(HttpClient http, IConfiguration config,
        ILogger<ContentGenerationService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    // ── Public API ─────────────────────────────────────────────────────────

    public Task<string> GenerateScriptAsync(string topic, ContentLanguage language,
        NicheCategory niche, string? style = null)
    {
        var lang = LangNames[language];
        var prompt =
            "You are a viral short-form video script writer for " + lang + " audiences.\n\n" +
            "Topic:    " + topic + "\n" +
            "Niche:    " + niche + "\n" +
            "Language: Write ENTIRELY in " + lang + "\n" +
            "Target:   45-60 seconds (~130 words)\n\n" +
            "Structure:\n" +
            "1. Hook - first 3 seconds: shocking fact, bold question, or power statement\n" +
            "2. Core content - 2-3 punchy insights or facts\n" +
            "3. CTA - like, follow, comment call-to-action in " + lang + "\n\n" +
            "Tone: fast-paced, energetic, native-speaker expressions only.\n" +
            (style != null ? "Style notes: " + style + "\n" : "") +
            "\nReturn ONLY the script. No stage directions, no labels.";

        return CallAsync(prompt);
    }

    public Task<string> GenerateTitleAsync(string topic, ContentLanguage language,
        NicheCategory niche)
    {
        var lang = LangNames[language];
        var prompt =
            "Create one viral YouTube Shorts title in " + lang + " for this topic: " + topic + "\n" +
            "Niche: " + niche + "\n\n" +
            "Rules:\n" +
            "- Max 70 characters\n" +
            "- Use numbers, power words, or emotional triggers\n" +
            "- Written entirely in " + lang + "\n" +
            "- Include one relevant emoji if suitable for the niche\n\n" +
            "Return ONLY the title.";

        return CallAsync(prompt);
    }

    public Task<string> GenerateDescriptionAsync(string title, string script,
        ContentLanguage language)
    {
        var lang = LangNames[language];
        var intro = script.Length > 300 ? script.Substring(0, 300) + "..." : script;
        var prompt =
            "Write a YouTube Shorts description in " + lang + ".\n" +
            "Title: " + title + "\n" +
            "Script snippet: " + intro + "\n\n" +
            "Requirements:\n" +
            "- 150-300 characters\n" +
            "- Strong call-to-action\n" +
            "- 3-5 keywords woven in naturally\n" +
            "- Entirely in " + lang + "\n\n" +
            "Return ONLY the description.";

        return CallAsync(prompt);
    }

    public Task<string> GenerateHashtagsAsync(string topic, ContentLanguage language,
        NicheCategory niche)
    {
        var lang = LangNames[language];
        var prompt =
            "Generate 18 hashtags for a " + lang + " " + niche + " short video about: " + topic + "\n\n" +
            "Include:\n" +
            "- 5 trending hashtags in " + lang + "\n" +
            "- 5 niche-specific hashtags\n" +
            "- 4 broad viral tags (#shorts #viral #trending #fyp)\n" +
            "- 4 platform tags (#YouTubeShorts #Reels #TikTok #Shortvideo)\n\n" +
            "Return ONLY space-separated hashtags, each starting with #.";

        return CallAsync(prompt);
    }

    public async Task<List<TrendTopicDto>> AnalyzeTrendsAsync(ContentLanguage language,
        NicheCategory niche)
    {
        var lang = LangNames[language];

        var jsonExample =
            "[\n" +
            "  {\n" +
            "    \"title\": \"...\",\n" +
            "    \"description\": \"2-sentence description in " + lang + "\",\n" +
            "    \"trendScore\": 85,\n" +
            "    \"keywords\": \"kw1, kw2, kw3, kw4, kw5\"\n" +
            "  }\n" +
            "]";

        var prompt =
            "Identify 10 currently trending topics for short-form video targeting " +
            lang + " speakers in the " + niche + " niche.\n\n" +
            "Return a JSON array (no markdown fences, no explanation):\n" +
            jsonExample;

        var raw = await CallAsync(prompt);

        try
        {
            var clean = raw.Replace("```json", "").Replace("```", "").Trim();
            var items = JsonSerializer.Deserialize<List<TrendItem>>(clean,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            return items.Select(x => new TrendTopicDto
            {
                Title = x.Title ?? string.Empty,
                Description = x.Description ?? string.Empty,
                TrendScore = x.TrendScore,
                Keywords = x.Keywords,
                Language = language,
                Niche = niche,
                Status = TrendStatus.Rising,
                DiscoveredAt = DateTime.UtcNow
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse trend JSON for {Lang}/{Niche}", language, niche);
            return new List<TrendTopicDto>();
        }
    }

    // ── Private ────────────────────────────────────────────────────────────

    private async Task<string> CallAsync(string prompt, int retryCount = 0)
    {
        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiKey);

        var body = new
        {
            model = Model,
            messages = new[] { new { role = "user", content = prompt } },
            max_tokens = 1200,
            temperature = 0.85
        };

        var resp = await _http.PostAsJsonAsync(
            "https://api.openai.com/v1/chat/completions", body);

        // 429 Too Many Requests → wait and retry (max 3 times)
        if (resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests && retryCount < 3)
        {
            var waitSeconds = (retryCount + 1) * 20;
            _logger.LogWarning("OpenAI rate limit (429) — retrying in {Sec}s (attempt {N}/3)",
                waitSeconds, retryCount + 1);
            await Task.Delay(TimeSpan.FromSeconds(waitSeconds));
            return await CallAsync(prompt, retryCount + 1);
        }

        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<OAIResponse>();
        return result?.Choices?.FirstOrDefault()?.Message?.Content?.Trim() ?? string.Empty;
    }

    // ── Response models ────────────────────────────────────────────────────

    private record TrendItem(
        string? Title,
        string? Description,
        double TrendScore,
        string? Keywords);

    private record OAIResponse(OAIChoice[]? Choices);
    private record OAIChoice(OAIMessage? Message);
    private record OAIMessage(string? Content);
}