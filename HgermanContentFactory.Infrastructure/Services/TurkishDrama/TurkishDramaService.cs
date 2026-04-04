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
/// Scene-aware: pulls real character names, relationships and iconic moments from DramaDb.
/// </summary>
public class TurkishDramaService : ITurkishDramaService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly AppDbContext _db;
    private readonly ILogger<TurkishDramaService> _logger;

    private string ApiKey => _config["Anthropic:ApiKey"]
        ?? throw new InvalidOperationException("Anthropic:ApiKey not configured.");

    private const string Model = "claude-sonnet-4-20250514";
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string ApiVersion = "2023-06-01";

    // ── Language metadata ──────────────────────────────────────────────────

    private static readonly Dictionary<ContentLanguage, LangMeta> Languages = new()
    {
        [ContentLanguage.Spanish] = new("Spanish (Español)", "es",
            "Latin America & Spain",
            "Telenovela culture: audiences want raw passion, family betrayal, impossible love. " +
            "Use second-person ('¿Recuerdas cuando...?'). Short punchy sentences. Exclamation marks work."),

        [ContentLanguage.French] = new("French (Français)", "fr",
            "France, Belgium, North Africa",
            "Intellectual + romantic: viewers want emotional nuance and moral tension. " +
            "Elegant phrasing, rhetorical questions ('Et si c'était trop tard?'). Restrained but deep."),

        [ContentLanguage.German] = new("German (Deutsch)", "de",
            "Germany, Austria, Switzerland",
            "Direct and impactful: no melodrama, just gut-punch facts and plot twists. " +
            "Short sentences, strong verbs. Address viewer ('Stell dir vor...'). Efficient storytelling."),

        [ContentLanguage.Italian] = new("Italian (Italiano)", "it",
            "Italy",
            "Cinema passion: family honor, forbidden love, sacrifice. " +
            "Expressive, cinematic language. Use 'tu' address. Dramatic pauses written as '...'"),

        [ContentLanguage.Polish] = new("Polish (Polski)", "pl",
            "Poland",
            "Slow-burn romance and longing: sacrifice, second chances, bittersweet emotion. " +
            "Poetic phrasing, address viewer ('Wyobraź sobie...'). Emotional not sensational."),

        [ContentLanguage.English] = new("English", "en",
            "UK, USA, Global",
            "Hook-driven: viewer has 2 seconds to decide. Lead with the drama, not the setup. " +
            "Reference 'Turkish drama' or 'diziler' as a trend. Fast, punchy, curiosity-gap opening."),
    };

    // ── Drama database: real characters, relationships, iconic scenes ──────

    private static readonly Dictionary<string, DramaInfo> DramaDb = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Sen Çal Kapımı"] = new(
            new[] { "Sen Çal Kapımı", "Love Is in the Air", "You Knock on My Door" },
            "Eda Yıldız & Serkan Bolat",
            "Eda (florist, impulsive, warm), Serkan (cold architect billionaire), " +
                        "Ayfer (Eda's aunt), Engin (Serkan's best friend), Piril (Engin's girlfriend), " +
                        "Selin (Serkan's ex-fiancée), Balca (rival), Melo (Eda's friend)",
            "Istanbul, Artlife architecture firm, Eda's flower shop",
            "Rom-com enemies-to-lovers with fake relationship trope",
            new[]
            {
                "Serkan fakes amnesia and falls for Eda all over again",
                "Eda and Serkan handcuffed together for 48 hours",
                "Serkan's cancer diagnosis — he pushes Eda away to protect her",
                "First kiss interrupted by a phone call",
                "Eda discovers Serkan erased her memories",
                "Serkan watches Eda from afar knowing she doesn't remember him",
                "The fake engagement becomes real feelings",
                "Serkan's mother disapproves — class conflict",
            }),

        ["Kara Sevda"] = new(
            new[] { "Kara Sevda", "Endless Love", "Neverending Love" },
            "Nihan Sezin & Kemal Soydere",
            "Nihan (artist, torn between love and family), Kemal (poor but honorable), " +
                        "Emir (Nihan's controlling husband, villain), Vildan (Nihan's mother), " +
                        "Leyla (Kemal's sister), Ozan (Nihan's troubled brother)",
            "Istanbul, Sezin family mansion vs. Kemal's humble neighborhood",
            "Tragic star-crossed love, class divide, obsessive antagonist",
            new[]
            {
                "Kemal chooses Nihan over Zeynep at the wedding — she marries Emir anyway",
                "Emir forces Nihan to stay by threatening Kemal's family",
                "Nihan paints Kemal's portrait in secret",
                "Kemal discovers Nihan's daughter is his child",
                "The airport goodbye — Kemal lets her go",
                "Emir burns Nihan's paintings",
                "Kemal returns after years — Nihan pretends not to care",
                "The final sacrifice — Kemal takes a bullet for Nihan",
            }),

        ["Yalı Çapkını"] = new(
            new[] { "Yalı Çapkını", "The Kingfisher", "Seyran and Ferit" },
            "Seyran Sarıoğlu & Ferit Korhan",
            "Seyran (modern, free-spirited), Ferit (playboy who falls unexpectedly), " +
                        "Kazım (Seyran's controlling father), Hünkar (Ferit's domineering grandmother), " +
                        "Pelin (Ferit's ex who returns), Orhan (Ferit's brother)",
            "Istanbul Bosphorus yalı (waterfront mansion), two rival families",
            "Forced marriage slow-burn, tradition vs. modernity",
            new[]
            {
                "Forced wedding — Seyran and Ferit meet at the altar",
                "Ferit realizes he's in love with his own wife",
                "Seyran catches Ferit with Pelin — heartbreak",
                "The first night Ferit actually talks to Seyran — wall cracks",
                "Seyran stands up to Hünkar for the first time",
                "Ferit's jealousy when another man looks at Seyran",
                "They sleep in the same room for the first time — tension",
                "Seyran threatens to leave — Ferit admits he can't let her go",
            }),

        ["Aşk-ı Memnu"] = new(
            new[] { "Aşk-ı Memnu", "Forbidden Love", "Bihter and Behlül" },
            "Bihter Ziyagil & Behlül Haznedar",
            "Bihter (beautiful, ambitious, tragic), Behlül (charming nephew, morally weak), " +
                        "Adnan (wealthy older husband), Nihal (Adnan's daughter, pure), " +
                        "Firdevs (Bihter's scheming mother)",
            "Bosphorus yalı, elite Istanbul society",
            "Forbidden affair, guilt, tragedy — Turkish classic literature",
            new[]
            {
                "Bihter and Behlül's first forbidden kiss",
                "Nihal discovers the affair — innocence shattered",
                "Bihter confesses to Adnan — he can't speak",
                "Behlül chooses Nihal's safety over Bihter — betrayal",
                "Bihter's tragic ending — the yalı balcony",
                "Firdevs manipulates Bihter into the marriage",
                "Behlül realizes too late what he destroyed",
            }),

        ["Diriliş Ertuğrul"] = new(
            new[] { "Diriliş Ertuğrul", "Resurrection Ertugrul", "Ertugrul" },
            "Ertuğrul Bey & Halime Hatun",
            "Ertuğrul (warrior leader, just and fearless), Halime (brave, loyal), " +
                        "Turgut (loyal alps companion), Bamsi (fierce alps brother), " +
                        "Noyan (Mongol antagonist), Gündüz (Ertuğrul's brother)",
            "13th century Anatolia, Kayı tribe, Byzantine and Mongol territories",
            "Historical epic, brotherhood, sacrifice for justice and faith",
            new[]
            {
                "Ertuğrul saves Halime from Crusader captivity",
                "The alps brotherhood oath scene",
                "Ertuğrul defeats Noyan in single combat",
                "Halime's death — Ertuğrul's grief shakes the tribe",
                "Ertuğrul refuses to bow to unjust power",
                "The tribe betrayed from within by a brother",
                "Suleyman Shah's burial at the Euphrates river",
            }),

        ["Çukur"] = new(
            new[] { "Çukur", "The Pit" },
            "Yamaç Koçovalı & Sena",
            "Yamaç (youngest Koçovalı, torn between love and family duty), " +
                        "Sena (his wife, kidnapped to control him), Cumali (violent older brother), " +
                        "İdris (patriarch), Vartolu (rival turned reluctant ally)",
            "Çukur neighborhood Istanbul, crime family underground",
            "Crime family drama, loyalty vs. love, dark brotherhood",
            new[]
            {
                "Yamaç dragged back into Çukur after leaving for love",
                "Sena kidnapped — Yamaç tears the city apart",
                "Brothers fight each other then bleed together",
                "Yamaç becomes the thing he swore he'd never be",
                "İdris sacrifices everything silently for his sons",
            }),

        ["Yemin"] = new(
            new[] { "Yemin", "The Promise", "Reyhan and Emir" },
            "Reyhan & Emir Tarhan",
            "Reyhan (poor, kind, quietly strong), Emir (cold wealthy man hiding pain), " +
                        "Narin (Emir's scheming ex), Gönül (Reyhan's friend), Hikmet (Emir's father)",
            "Tarhan mansion, class divide between wealthy and poor Istanbul",
            "Forced marriage, cold-to-warm redemption arc",
            new[]
            {
                "Emir marries Reyhan for revenge — then falls for her",
                "Reyhan forgives Emir after cruelty — he's undone by it",
                "Emir sees Reyhan cry and something cracks inside him",
                "Narin's scheme exposed — Emir shields Reyhan",
                "Emir's first genuine 'I'm sorry' — Reyhan doesn't know what to do",
                "Reyhan nearly leaves — Emir stops her at the door without words",
            }),

        ["Muhtesem Yuzyil"] = new(
            new[] { "Muhtesem Yuzyil", "Magnificent Century", "Suleiman and Hurrem" },
            "Sultan Suleiman & Hürrem Sultan",
            "Suleiman (Ottoman Sultan, powerful, conflicted), " +
                        "Hürrem (slave who becomes queen, brilliant strategist), " +
                        "İbrahim Paşa (Suleiman's best friend, Hürrem's rival), " +
                        "Mahidevran (first wife, displaced by Hürrem), Şehzade Mustafa (tragic prince)",
            "Topkapı Palace Istanbul, 16th century Ottoman Empire",
            "Palace intrigue, power, love as political weapon, historical tragedy",
            new[]
            {
                "Hürrem laughs in the harem — Suleiman is instantly captivated",
                "Hürrem converts — Suleiman breaks centuries of protocol for her",
                "İbrahim Paşa's execution — ordered by the man who loved him",
                "Mahidevran vs. Hürrem — the harem war for Suleiman's heart",
                "Şehzade Mustafa's death — the empire weeps",
                "Hürrem's letter to Suleiman during his longest war campaign",
            }),

        ["Zalim İstanbul"] = new(
            new[] { "Zalim İstanbul", "Ruthless Istanbul", "Cruel Istanbul" },
            "Cenk Karaçay & Seher",
            "Seher (poor girl entering elite world with a secret mission), " +
                        "Agah (family patriarch hiding murderous secrets), " +
                        "Cenk (Agah's son, falls for Seher not knowing her true motive), " +
                        "Nedim (corrupt elite enforcer)",
            "Elite Istanbul mansions vs. poor neighborhood, class war backdrop",
            "Revenge infiltration, class war, dark secrets, Seher's transformation",
            new[]
            {
                "Seher discovers Agah killed her entire family",
                "Seher infiltrates the Karaçay family to destroy them from inside",
                "Cenk falls in love — not knowing Seher came to ruin them",
                "Seher must choose: finish the revenge or save Cenk",
                "Agah's crime revealed to his own children",
            }),

        ["Kurt Seyit ve Şura"] = new(
            new[] { "Kurt Seyit ve Şura", "Kurt Seyit and Shura" },
            "Kurt Seyit & Şura (Alexandra)",
            "Seyit (Turkish cavalry officer, noble and loyal), " +
                        "Şura (Russian aristocrat, brave in chaos), " +
                        "Petro (Şura's other suitor, persistent), Celil (Seyit's friend)",
            "WWI era Crimea, Russian Revolution, Istanbul exile",
            "Historical romance, war separation, love across borders and empires",
            new[]
            {
                "Seyit and Şura meet at a Crimean ball — both feel it instantly",
                "Revolution forces Şura to flee — Seyit crosses frontlines to find her",
                "Seyit torn between military duty and the woman he loves",
                "The station goodbye — neither knows if they'll ever meet again",
                "Seyit unable to keep his promise — war took everything",
            }),
    };

    public TurkishDramaService(HttpClient http, IConfiguration config,
        AppDbContext db, ILogger<TurkishDramaService> logger)
    {
        _http = http;
        _config = config;
        _db = db;
        _logger = logger;
    }

    // ── ITurkishDramaService ───────────────────────────────────────────────

    public async Task<List<TurkishDramaTrendDto>> GetTrendingDramasAsync(ContentLanguage language)
    {
        var meta = GetMeta(language);

        var knownSeriesList = string.Join("\n", DramaDb.Select(kv =>
            $"- {kv.Key} | Couple: {kv.Value.MainCouple} | Tone: {kv.Value.Tone}"));

        var prompt =
            $"You are a social media trend analyst specializing in Turkish TV dramas (dizi) " +
            $"for {meta.Name}-speaking markets ({meta.Market}).\n\n" +
            $"Audience profile: {meta.Insight}\n\n" +
            $"Drama database (prioritize these — you know their real characters and scenes):\n" +
            $"{knownSeriesList}\n\n" +
            $"Identify 8 SPECIFIC Turkish drama scenes or emotional moments viral RIGHT NOW " +
            $"for {meta.Name} audiences.\n\n" +
            $"Be SPECIFIC — name the exact scene, character action, or emotional beat. " +
            $"NOT 'romantic moment' — instead: 'Serkan watches Eda sleep not knowing she remembers him'.\n\n" +
            $"Return a JSON array (no markdown, no explanation):\n" +
            $"[\n" +
            $"  {{\n" +
            $"    \"seriesName\": \"Exact series name from database or well-known dizi\",\n" +
            $"    \"sceneOrEmotion\": \"Specific scene, 10-14 words, present tense\",\n" +
            $"    \"whyViral\": \"1 sentence: emotional reason this hits {meta.Name} viewers hard\",\n" +
            $"    \"trendScore\": 0-100,\n" +
            $"    \"keywords\": \"5 comma-separated {meta.Code} keywords for this scene\"\n" +
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
                SeriesName = x.SeriesName ?? string.Empty,
                SceneOrEmotion = x.SceneOrEmotion ?? string.Empty,
                WhyViral = x.WhyViral ?? string.Empty,
                TrendScore = x.TrendScore,
                Keywords = x.Keywords ?? string.Empty,
                Language = language,
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
        var dramaContext = GetDramaContext(req.DramaSeries);
        var prompt = BuildContentPrompt(req.DramaSeries, req.SceneOrEmotion, meta, dramaContext);

        ContentRaw? parsed = null;
        try
        {
            var raw = await CallClaudeAsync(prompt, maxTokens: 1200);
            var clean = raw.Replace("```json", "").Replace("```", "").Trim();
            parsed = JsonSerializer.Deserialize<ContentRaw>(clean,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Content generation failed for {Series}/{Lang}",
                req.DramaSeries, req.Language);
            return new TurkishDramaContentDto
            {
                Language = req.Language,
                DramaSeries = req.DramaSeries,
                SceneOrEmotion = req.SceneOrEmotion,
                ErrorMessage = ex.Message
            };
        }

        var dto = new TurkishDramaContentDto
        {
            Language = req.Language,
            DramaSeries = req.DramaSeries,
            SceneOrEmotion = req.SceneOrEmotion,
            Title = parsed?.Title ?? string.Empty,
            Script = parsed?.Script ?? string.Empty,
            Description = parsed?.Description ?? string.Empty,
            Hashtags = parsed?.Hashtags ?? string.Empty,
            Hook = parsed?.Hook ?? string.Empty,
            CallToAction = parsed?.CallToAction ?? string.Empty,
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
                DramaSeries = dramaSeries,
                SceneOrEmotion = sceneOrEmotion,
                Language = lang,
                AutoSave = false,
            }));

        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }

    // ── Prompt builder ─────────────────────────────────────────────────────

    private static string BuildContentPrompt(
        string series, string scene, LangMeta meta, string dramaContext)
    {
        return
            $"You are a viral YouTube Shorts scriptwriter for Turkish drama fan channels, " +
            $"writing for {meta.Name}-speaking audiences ({meta.Market}).\n\n" +

            $"=== DRAMA CONTEXT ===\n" +
            $"{dramaContext}\n\n" +

            $"=== THIS VIDEO ===\n" +
            $"Series : {series}\n" +
            $"Scene  : {scene}\n\n" +

            $"=== AUDIENCE ===\n" +
            $"{meta.Insight}\n\n" +

            $"=== SCRIPT RULES ===\n" +
            $"- Write ENTIRELY in {meta.Name} — zero English or Turkish words (series name OK)\n" +
            $"- Target: 70-85 words total (fits ~45 second Short)\n" +
            $"- Address the VIEWER directly — pull them into the scene\n" +
            $"- Use REAL character names from the drama context above\n" +
            $"- Structure: Hook (1 sentence, shock/question) → " +
            $"Scene (2-3 sentences, present tense as if happening NOW) → " +
            $"Emotional twist (1 sentence) → CTA (1 sentence)\n" +
            $"- NO intro like 'In this drama...' or 'Today we talk about...'\n" +
            $"- Start MID-ACTION, inside the tension of the scene\n" +
            $"- Hashtags: 4 {meta.Code} drama tags + 4 niche + " +
            $"4 platform (#shorts #viral #fyp #youtubeshorts) + 3 Turkish drama specific\n\n" +

            $"Return ONLY this JSON (no markdown fences, no extra text):\n" +
            $"{{\n" +
            $"  \"title\": \"Title in {meta.Name} — max 65 chars, 1 emoji, tease the scene\",\n" +
            $"  \"hook\": \"Opening line only — first 3 seconds, 1 punchy sentence\",\n" +
            $"  \"script\": \"Full script in {meta.Name}, 70-85 words, NO stage directions\",\n" +
            $"  \"callToAction\": \"Closing CTA in {meta.Name} — encourage comment or follow\",\n" +
            $"  \"description\": \"YouTube description in {meta.Name}, 120-200 chars\",\n" +
            $"  \"hashtags\": \"All hashtags space-separated starting with #\"\n" +
            $"}}";
    }

    // ── Drama context lookup ───────────────────────────────────────────────

    private static string GetDramaContext(string seriesInput)
    {
        var baseName = seriesInput.Split('(')[0].Trim();

        var key = DramaDb.Keys.FirstOrDefault(k =>
            seriesInput.Contains(k, StringComparison.OrdinalIgnoreCase) ||
            k.Contains(baseName, StringComparison.OrdinalIgnoreCase));

        if (key == null)
        {
            key = DramaDb.FirstOrDefault(kv =>
                kv.Value.Aliases.Any(a =>
                    seriesInput.Contains(a, StringComparison.OrdinalIgnoreCase) ||
                    a.Contains(baseName, StringComparison.OrdinalIgnoreCase))
            ).Key;
        }

        if (key == null)
            return $"Series: {seriesInput}\n(No detailed context available — use your knowledge of this Turkish drama.)";

        var d = DramaDb[key];
        return
            $"Series       : {key}\n" +
            $"Main couple  : {d.MainCouple}\n" +
            $"Characters   : {d.Characters}\n" +
            $"Setting      : {d.Setting}\n" +
            $"Tone         : {d.Tone}\n" +
            $"Iconic scenes: {string.Join(" | ", d.IconicScenes)}";
    }

    // ── DB save ────────────────────────────────────────────────────────────

    private async Task<int?> SaveVideoAsync(TurkishDramaContentDto dto, int channelId)
    {
        try
        {
            var video = new CF_Video
            {
                Title = dto.Title,
                Description = dto.Description,
                Script = dto.Script,
                Hashtags = dto.Hashtags,
                Language = dto.Language,
                Niche = NicheCategory.TurkishDrama,
                ChannelId = channelId,
                Status = VideoStatus.ScriptReady,
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

    // ── Claude API ─────────────────────────────────────────────────────────

    private async Task<string> CallClaudeAsync(string prompt, int maxTokens = 1200,
        int retryCount = 0)
    {
        _http.DefaultRequestHeaders.Clear();
        _http.DefaultRequestHeaders.Add("x-api-key", ApiKey);
        _http.DefaultRequestHeaders.Add("anthropic-version", ApiVersion);

        var body = new
        {
            model = Model,
            max_tokens = maxTokens,
            messages = new[] { new { role = "user", content = prompt } }
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

    private record DramaInfo(
        string[] Aliases,
        string MainCouple,
        string Characters,
        string Setting,
        string Tone,
        string[] IconicScenes);

    private record TrendRaw(
        string? SeriesName,
        string? SceneOrEmotion,
        string? WhyViral,
        double TrendScore,
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