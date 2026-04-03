using HgermanContentFactory.Core.DTOs;
using HgermanContentFactory.Core.Enums;
using HgermanContentFactory.Core.Interfaces;
using HgermanContentFactory.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HgermanContentFactory.Web.Controllers;

public class TurkishDramaController : Controller
{
    private readonly ITurkishDramaService _dramaService;
    private readonly AppDbContext         _db;
    private readonly ILogger<TurkishDramaController> _logger;

    // Popular series for dropdown
    private static readonly List<string> KnownSeries = new()
    {
        "Kara Sevda (Endless Love)",
        "Aşk-ı Memnu (Forbidden Love)",
        "Sen Çal Kapımı (Love Is in the Air)",
        "Yalı Çapkını (The Kingfisher)",
        "Diriliş Ertuğrul (Resurrection Ertugrul)",
        "Muhtesem Yuzyil (Magnificent Century)",
        "Çukur (The Pit)",
        "Yemin (The Promise)",
        "Zalim İstanbul (Ruthless Istanbul)",
        "Kurt Seyit ve Şura",
        "Ezel",
        "Fatih Harbiye",
        "Barbaroslar (Barbarossa)",
        "Bir Zamanlar Çukurova",
        "Içerde (Inside)",
    };

    public TurkishDramaController(ITurkishDramaService dramaService,
        AppDbContext db, ILogger<TurkishDramaController> logger)
    {
        _dramaService = dramaService;
        _db           = db;
        _logger       = logger;
    }

    // GET /TurkishDrama
    public IActionResult Index()
    {
        ViewData["Title"] = "Turkish Drama Shorts";
        return View();
    }

    // GET /TurkishDrama/Trends
    public IActionResult Trends()
    {
        ViewData["Title"] = "Drama Trend Analyzer";
        ViewBag.Languages = GetLanguageList();
        return View();
    }

    // POST /TurkishDrama/GetTrends  (AJAX)
    [HttpPost]
    public async Task<IActionResult> GetTrends([FromBody] GetTrendsRequest req)
    {
        try
        {
            var trends = await _dramaService.GetTrendingDramasAsync(req.Language);
            return Json(new { success = true, trends });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetTrends failed for {Lang}", req.Language);
            return Json(new { success = false, error = ex.Message });
        }
    }

    // GET /TurkishDrama/Generate
    public async Task<IActionResult> Generate()
    {
        ViewData["Title"] = "Generate Drama Content";
        await PopulateGenerateViewBag();
        return View(new TurkishDramaGenerateRequest());
    }

    // POST /TurkishDrama/Generate  (single language)
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Generate(TurkishDramaGenerateRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.DramaSeries) ||
            string.IsNullOrWhiteSpace(req.SceneOrEmotion))
        {
            ModelState.AddModelError("", "Drama series and scene/emotion are required.");
            await PopulateGenerateViewBag();
            return View(req);
        }

        var result = await _dramaService.GenerateContentAsync(req);

        if (result.ErrorMessage != null)
        {
            ModelState.AddModelError("", $"Generation failed: {result.ErrorMessage}");
            await PopulateGenerateViewBag();
            return View(req);
        }

        TempData["Success"] = $"Content generated for {result.LanguageName}!";
        return View("Result", new List<TurkishDramaContentDto> { result });
    }

    // POST /TurkishDrama/GenerateBulk  (all 6 languages at once)
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateBulk(string dramaSeries, string sceneOrEmotion)
    {
        if (string.IsNullOrWhiteSpace(dramaSeries) || string.IsNullOrWhiteSpace(sceneOrEmotion))
        {
            TempData["Error"] = "Drama series and scene/emotion are required.";
            return RedirectToAction(nameof(Generate));
        }

        ViewData["Title"] = "Bulk Content — 6 Languages";
        var results = await _dramaService.GenerateBulkAsync(dramaSeries, sceneOrEmotion);
        return View("Result", results);
    }

    // POST /TurkishDrama/SaveVideo  (AJAX — save one result to CF_Videos)
    [HttpPost]
    public async Task<IActionResult> SaveVideo([FromBody] SaveDramaVideoRequest req)
    {
        try
        {
            var genReq = new TurkishDramaGenerateRequest
            {
                DramaSeries    = req.DramaSeries,
                SceneOrEmotion = req.SceneOrEmotion,
                Language       = req.Language,
                ChannelId      = req.ChannelId,
                AutoSave       = true,
            };

            // Re-use the already-generated content by faking it as a generate call
            // OR just save directly via a lightweight path:
            var result = await _dramaService.GenerateContentAsync(genReq);
            return Json(new { success = true, videoId = result.SavedVideoId });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    // GET /TurkishDrama/History
    public async Task<IActionResult> History(ContentLanguage? language, int page = 1)
    {
        ViewData["Title"] = "Turkish Drama History";
        const int pageSize = 20;

        var q = _db.CF_Videos
            .Include(v => v.Channel)
            .Where(v => v.IsActive && v.Niche == NicheCategory.TurkishDrama);

        if (language.HasValue)
            q = q.Where(v => v.Language == language.Value);

        var total  = await q.CountAsync();
        var videos = await q.OrderByDescending(v => v.CreatedAt)
                             .Skip((page - 1) * pageSize).Take(pageSize)
                             .ToListAsync();

        ViewBag.Total      = total;
        ViewBag.Page       = page;
        ViewBag.PageSize   = pageSize;
        ViewBag.Languages  = GetLanguageList(language);
        ViewBag.Selected   = language;
        return View(videos);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private async Task PopulateGenerateViewBag()
    {
        ViewBag.KnownSeries = KnownSeries
            .Select(s => new SelectListItem(s, s)).ToList();

        ViewBag.Languages = GetLanguageList();

        ViewBag.Channels = await _db.CF_Channels
            .Where(c => c.IsActive)
            .Select(c => new SelectListItem(
                $"{c.Name} ({c.Language})", c.Id.ToString()))
            .ToListAsync();
    }

    private static List<SelectListItem> GetLanguageList(ContentLanguage? selected = null) =>
        new List<(ContentLanguage lang, string label, string flag)>
        {
            (ContentLanguage.Spanish, "Español",  "🇪🇸"),
            (ContentLanguage.French,  "Français",  "🇫🇷"),
            (ContentLanguage.German,  "Deutsch",   "🇩🇪"),
            (ContentLanguage.Italian, "Italiano",  "🇮🇹"),
            (ContentLanguage.Polish,  "Polski",    "🇵🇱"),
            (ContentLanguage.English, "English",   "🇬🇧"),
        }
        .Select(x => new SelectListItem(
            $"{x.flag} {x.label}",
            ((int)x.lang).ToString(),
            selected.HasValue && selected.Value == x.lang))
        .ToList();
}

// ── Request records ────────────────────────────────────────────────────────

public record GetTrendsRequest(ContentLanguage Language);
public record SaveDramaVideoRequest(
    string DramaSeries,
    string SceneOrEmotion,
    ContentLanguage Language,
    int ChannelId);
