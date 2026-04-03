using HgermanContentFactory.Core.DTOs;
using HgermanContentFactory.Core.Enums;
using HgermanContentFactory.Core.Interfaces;
using HgermanContentFactory.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HgermanContentFactory.Web.Controllers;

public class VideosController : Controller
{
    private readonly AppDbContext _db;
    private readonly IVideoGenerationService _videoGen;
    private readonly ILogger<VideosController> _logger;

    public VideosController(AppDbContext db, IVideoGenerationService videoGen,
        ILogger<VideosController> logger)
    {
        _db       = db;
        _videoGen = videoGen;
        _logger   = logger;
    }

    // GET /Videos
    public async Task<IActionResult> Index(int? channelId, VideoStatus? status, int page = 1)
    {
        const int pageSize = 20;
        var q = _db.CF_Videos
            .Include(v => v.Channel)
            .Include(v => v.TrendTopic)
            .Where(v => v.IsActive);

        if (channelId.HasValue) q = q.Where(v => v.ChannelId == channelId.Value);
        if (status.HasValue)    q = q.Where(v => v.Status    == status.Value);

        var total   = await q.CountAsync();
        var videos  = await q.OrderByDescending(v => v.CreatedAt)
                             .Skip((page - 1) * pageSize).Take(pageSize)
                             .ToListAsync();

        ViewBag.Total      = total;
        ViewBag.Page       = page;
        ViewBag.PageSize   = pageSize;
        ViewBag.ChannelId  = channelId;
        ViewBag.Status     = status;
        ViewBag.Channels   = await _db.CF_Channels.Where(c => c.IsActive)
                                       .Select(c => new SelectListItem(c.Name, c.Id.ToString()))
                                       .ToListAsync();
        ViewBag.Statuses   = Enum.GetValues<VideoStatus>()
                                  .Select(s => new SelectListItem(s.ToString(), ((int)s).ToString()));
        return View(videos);
    }

    // GET /Videos/Details/5
    public async Task<IActionResult> Details(int id)
    {
        var v = await _db.CF_Videos
            .Include(v => v.Channel)
            .Include(v => v.TrendTopic)
            .Include(v => v.Campaign)
            .FirstOrDefaultAsync(v => v.Id == id);
        return v == null ? NotFound() : View(v);
    }

    // GET /Videos/Generate
    public async Task<IActionResult> Generate(int? channelId)
    {
        await PopulateGenerateDropdowns(channelId);
        return View(new GenerateVideoRequestDto { ChannelId = channelId ?? 0 });
    }

    // POST /Videos/Generate
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Generate(GenerateVideoRequestDto dto)
    {
        if (!ModelState.IsValid)
        {
            await PopulateGenerateDropdowns(dto.ChannelId);
            return View(dto);
        }

        try
        {
            var video = await _videoGen.GenerateAsync(dto);
            TempData["Success"] = $"Video \"{video.Title}\" generated successfully.";
            return RedirectToAction(nameof(Details), new { id = video.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Video generation failed");
            ModelState.AddModelError("", $"Generation failed: {ex.Message}");
            await PopulateGenerateDropdowns(dto.ChannelId);
            return View(dto);
        }
    }

    // POST /Videos/GenerateBulk  (AJAX)
    [HttpPost]
    public async Task<IActionResult> GenerateBulk([FromBody] BulkGenerateRequest req)
    {
        var results = new List<object>();
        for (int i = 0; i < req.Count; i++)
        {
            try
            {
                var v = await _videoGen.GenerateAsync(new GenerateVideoRequestDto
                {
                    ChannelId   = req.ChannelId,
                    AutoPublish = req.AutoPublish
                });
                results.Add(new { id = v.Id, title = v.Title, success = true });
            }
            catch (Exception ex)
            {
                results.Add(new { success = false, error = ex.Message });
            }
        }
        return Json(new { results });
    }

    // POST /Videos/Publish/5  (AJAX)
    [HttpPost]
    public async Task<IActionResult> Publish(int id)
    {
        var ok = await _videoGen.PublishAsync(id);
        var v  = await _db.CF_Videos.FindAsync(id);
        return Json(new
        {
            success    = ok,
            status     = v?.Status.ToString(),
            youtubeUrl = v?.YouTubeVideoId != null ? $"https://youtu.be/{v.YouTubeVideoId}" : null,
            error      = v?.ErrorMessage
        });
    }

    // POST /Videos/Render/5  (AJAX)
    [HttpPost]
    public async Task<IActionResult> Render(int id)
    {
        var ok = await _videoGen.RenderAsync(id);
        return Json(new { success = ok });
    }

    // POST /Videos/Cancel/5
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var v = await _db.CF_Videos.FindAsync(id);
        if (v != null && v.Status is VideoStatus.Pending or VideoStatus.Scheduled or VideoStatus.ScriptReady)
        {
            v.Status    = VideoStatus.Cancelled;
            v.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
        TempData["Success"] = "Video cancelled.";
        return RedirectToAction(nameof(Index));
    }

    // POST /Videos/Delete/5
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var v = await _db.CF_Videos.FindAsync(id);
        if (v != null) { v.IsActive = false; v.UpdatedAt = DateTime.UtcNow; await _db.SaveChangesAsync(); }
        TempData["Success"] = "Video deleted.";
        return RedirectToAction(nameof(Index));
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private async Task PopulateGenerateDropdowns(int? channelId)
    {
        ViewBag.Channels = await _db.CF_Channels
            .Where(c => c.IsActive)
            .Select(c => new SelectListItem(c.Name, c.Id.ToString()))
            .ToListAsync();

        ViewBag.Trends = channelId.HasValue
            ? await _db.CF_TrendTopics
                .Include(t => t.Videos)
                .Where(t => t.IsActive)
                .OrderByDescending(t => t.TrendScore)
                .Take(20)
                .Select(t => new SelectListItem(
                    $"[{t.TrendScore:F0}] {t.Title} ({t.Language})", t.Id.ToString()))
                .ToListAsync()
            : new List<SelectListItem>();
    }
}

public record BulkGenerateRequest(int ChannelId, int Count, bool AutoPublish);
