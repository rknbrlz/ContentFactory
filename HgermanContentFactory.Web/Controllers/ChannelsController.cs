using HgermanContentFactory.Core.DTOs;
using HgermanContentFactory.Core.Entities;
using HgermanContentFactory.Core.Enums;
using HgermanContentFactory.Core.Interfaces;
using HgermanContentFactory.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HgermanContentFactory.Web.Controllers;

public class ChannelsController : Controller
{
    private readonly AppDbContext _db;
    private readonly IYouTubeService _youtube;
    private readonly ILogger<ChannelsController> _logger;

    public ChannelsController(AppDbContext db, IYouTubeService youtube,
        ILogger<ChannelsController> logger)
    {
        _db      = db;
        _youtube = youtube;
        _logger  = logger;
    }

    // GET /Channels
    public async Task<IActionResult> Index()
    {
        var channels = await _db.CF_Channels
            .Where(c => c.IsActive)
            .OrderBy(c => c.Language).ThenBy(c => c.Niche)
            .ToListAsync();
        return View(channels);
    }

    // GET /Channels/Create
    public IActionResult Create()
    {
        PopulateDropdowns();
        return View(new CreateChannelDto());
    }

    // POST /Channels/Create
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateChannelDto dto)
    {
        if (!ModelState.IsValid) { PopulateDropdowns(); return View(dto); }

        var channel = new CF_Channel
        {
            Name             = dto.Name,
            Description      = dto.Description,
            Language         = dto.Language,
            Niche            = dto.Niche,
            Platform         = dto.Platform,
            DailyVideoTarget = dto.DailyVideoTarget,
            DefaultHashtags  = dto.DefaultHashtags,
            CreatedAt        = DateTime.UtcNow
        };
        _db.CF_Channels.Add(channel);
        await _db.SaveChangesAsync();

        _db.CF_Schedules.Add(new CF_Schedule
        {
            ChannelId    = channel.Id,
            Frequency    = ScheduleFrequency.Daily,
            VideosPerDay = dto.DailyVideoTarget,
            PublishTimes = "09:00",
            NextRun      = DateTime.UtcNow.Date.AddDays(1).AddHours(9)
        });
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Channel \"{channel.Name}\" created successfully.";
        return RedirectToAction(nameof(Index));
    }

    // GET /Channels/Edit/5
    public async Task<IActionResult> Edit(int id)
    {
        var ch = await _db.CF_Channels.FindAsync(id);
        if (ch == null) return NotFound();
        PopulateDropdowns();
        var dto = new EditChannelDto
        {
            Id               = ch.Id,
            Name             = ch.Name,
            Description      = ch.Description,
            Language         = ch.Language,
            Niche            = ch.Niche,
            Platform         = ch.Platform,
            DailyVideoTarget = ch.DailyVideoTarget,
            DefaultHashtags  = ch.DefaultHashtags
        };
        return View(dto);
    }

    // POST /Channels/Edit/5
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, EditChannelDto dto)
    {
        if (!ModelState.IsValid) { PopulateDropdowns(); return View(dto); }

        var ch = await _db.CF_Channels.FindAsync(id);
        if (ch == null) return NotFound();

        ch.Name             = dto.Name;
        ch.Description      = dto.Description;
        ch.Language         = dto.Language;
        ch.Niche            = dto.Niche;
        ch.DailyVideoTarget = dto.DailyVideoTarget;
        ch.DefaultHashtags  = dto.DefaultHashtags;
        ch.UpdatedAt        = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var sched = await _db.CF_Schedules.FirstOrDefaultAsync(s => s.ChannelId == id);
        if (sched != null) { sched.VideosPerDay = dto.DailyVideoTarget; await _db.SaveChangesAsync(); }

        TempData["Success"] = "Channel updated.";
        return RedirectToAction(nameof(Index));
    }

    // POST /Channels/UpdateDailyTarget  (AJAX)
    [HttpPost]
    public async Task<IActionResult> UpdateDailyTarget([FromBody] UpdateDailyTargetRequest req)
    {
        var ch = await _db.CF_Channels.FindAsync(req.ChannelId);
        if (ch == null) return Json(new { success = false, message = "Channel not found" });

        ch.DailyVideoTarget = Math.Clamp(req.VideosPerDay, 1, 50);
        ch.UpdatedAt        = DateTime.UtcNow;

        var sched = await _db.CF_Schedules.FirstOrDefaultAsync(s => s.ChannelId == req.ChannelId);
        if (sched != null) sched.VideosPerDay = ch.DailyVideoTarget;

        await _db.SaveChangesAsync();
        return Json(new { success = true, dailyTarget = ch.DailyVideoTarget });
    }

    // GET /Channels/ConnectYouTube/5
    public async Task<IActionResult> ConnectYouTube(int id)
    {
        var redirect = $"{Request.Scheme}://{Request.Host}/Channels/YouTubeCallback";
        var url      = await _youtube.GetAuthorizationUrlAsync(id, redirect);
        return Redirect(url);
    }

    // GET /Channels/YouTubeCallback
    public async Task<IActionResult> YouTubeCallback(string code, string state)
    {
        var channelId = int.Parse(state);
        var redirect  = $"{Request.Scheme}://{Request.Host}/Channels/YouTubeCallback";
        var ok        = await _youtube.ExchangeCodeForTokenAsync(channelId, code, redirect);

        TempData[ok ? "Success" : "Error"] =
            ok ? "YouTube connected successfully!" : "YouTube connection failed. Please try again.";

        return RedirectToAction(nameof(Index));
    }

    // POST /Channels/Delete/5
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var ch = await _db.CF_Channels.FindAsync(id);
        if (ch != null) { ch.IsActive = false; ch.UpdatedAt = DateTime.UtcNow; await _db.SaveChangesAsync(); }
        TempData["Success"] = "Channel deleted.";
        return RedirectToAction(nameof(Index));
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void PopulateDropdowns()
    {
        ViewBag.Languages = Enum.GetValues<ContentLanguage>()
            .Select(l => new SelectListItem(l.ToString(), ((int)l).ToString()));
        ViewBag.Niches = Enum.GetValues<NicheCategory>()
            .Select(n => new SelectListItem(n.ToString(), ((int)n).ToString()));
        ViewBag.Platforms = Enum.GetValues<PublishPlatform>()
            .Select(p => new SelectListItem(p.ToString(), ((int)p).ToString()));
    }
}

public record UpdateDailyTargetRequest(int ChannelId, int VideosPerDay);
