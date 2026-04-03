using HgermanContentFactory.Core.Enums;
using HgermanContentFactory.Core.Interfaces;
using HgermanContentFactory.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HgermanContentFactory.Web.Controllers;

public class TrendsController : Controller
{
    private readonly AppDbContext _db;
    private readonly ITrendAnalysisService _trendService;

    public TrendsController(AppDbContext db, ITrendAnalysisService trendService)
    {
        _db          = db;
        _trendService = trendService;
    }

    public async Task<IActionResult> Index(ContentLanguage? language, NicheCategory? niche)
    {
        var q = _db.CF_TrendTopics.Where(t => t.IsActive);
        if (language.HasValue) q = q.Where(t => t.Language == language.Value);
        if (niche.HasValue)    q = q.Where(t => t.Niche    == niche.Value);

        var trends = await q.OrderByDescending(t => t.TrendScore).Take(100).ToListAsync();

        ViewBag.Languages = Enum.GetValues<ContentLanguage>()
            .Select(l => new SelectListItem(l.ToString(), ((int)l).ToString()));
        ViewBag.Niches    = Enum.GetValues<NicheCategory>()
            .Select(n => new SelectListItem(n.ToString(), ((int)n).ToString()));
        ViewBag.SelectedLanguage = language;
        ViewBag.SelectedNiche    = niche;

        return View(trends);
    }

    // POST /Trends/Refresh  (AJAX)
    [HttpPost]
    public async Task<IActionResult> Refresh([FromBody] RefreshTrendsRequest req)
    {
        var trends = await _trendService.GetTrendingAsync(req.Language, req.Niche);
        return Json(new { success = true, count = trends.Count, trends });
    }

    // POST /Trends/RefreshAll
    [HttpPost]
    public async Task<IActionResult> RefreshAll()
    {
        await _trendService.RefreshAllTrendsAsync();
        TempData["Success"] = "Trends refreshed for all active channels.";
        return RedirectToAction(nameof(Index));
    }

    // POST /Trends/Delete/5
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var t = await _db.CF_TrendTopics.FindAsync(id);
        if (t != null) { t.IsActive = false; t.UpdatedAt = DateTime.UtcNow; await _db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Index));
    }
}

public record RefreshTrendsRequest(ContentLanguage Language, NicheCategory Niche);
