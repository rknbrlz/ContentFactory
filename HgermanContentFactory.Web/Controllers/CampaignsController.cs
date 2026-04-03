using HgermanContentFactory.Core.DTOs;
using HgermanContentFactory.Core.Entities;
using HgermanContentFactory.Core.Interfaces;
using HgermanContentFactory.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HgermanContentFactory.Web.Controllers;

public class CampaignsController : Controller
{
    private readonly AppDbContext _db;
    private readonly IVideoGenerationService _videoGen;

    public CampaignsController(AppDbContext db, IVideoGenerationService videoGen)
    {
        _db       = db;
        _videoGen = videoGen;
    }

    public async Task<IActionResult> Index()
    {
        var campaigns = await _db.CF_Campaigns
            .Include(c => c.Channel)
            .Where(c => c.IsActive)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();
        return View(campaigns);
    }

    public async Task<IActionResult> Details(int id)
    {
        var campaign = await _db.CF_Campaigns
            .Include(c => c.Channel)
            .Include(c => c.Videos)
            .FirstOrDefaultAsync(c => c.Id == id);
        return campaign == null ? NotFound() : View(campaign);
    }

    public async Task<IActionResult> Create()
    {
        await PopulateDropdowns();
        return View(new CreateCampaignDto { StartDate = DateTime.UtcNow });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateCampaignDto dto)
    {
        if (!ModelState.IsValid) { await PopulateDropdowns(); return View(dto); }

        var channel = await _db.CF_Channels.FindAsync(dto.ChannelId);
        if (channel == null) { ModelState.AddModelError("ChannelId", "Channel not found"); return View(dto); }

        int daysCount = dto.EndDate.HasValue
            ? (int)(dto.EndDate.Value - dto.StartDate).TotalDays + 1
            : 30;

        var campaign = new CF_Campaign
        {
            Name               = dto.Name,
            Description        = dto.Description,
            ChannelId          = dto.ChannelId,
            StartDate          = dto.StartDate,
            EndDate            = dto.EndDate,
            VideosPerDay       = dto.VideosPerDay,
            TotalVideosPlanned = dto.VideosPerDay * daysCount,
            AutoPublish        = dto.AutoPublish,
            ContentStyle       = dto.ContentStyle,
            TargetAudience     = dto.TargetAudience,
            CreatedAt          = DateTime.UtcNow
        };

        _db.CF_Campaigns.Add(campaign);
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Campaign \"{campaign.Name}\" created.";
        return RedirectToAction(nameof(Details), new { id = campaign.Id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var c = await _db.CF_Campaigns.FindAsync(id);
        if (c != null) { c.IsActive = false; c.UpdatedAt = DateTime.UtcNow; await _db.SaveChangesAsync(); }
        TempData["Success"] = "Campaign deleted.";
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateDropdowns()
    {
        ViewBag.Channels = await _db.CF_Channels
            .Where(c => c.IsActive)
            .Select(c => new SelectListItem(
                $"{c.Name} ({c.Language} / {c.Niche})", c.Id.ToString()))
            .ToListAsync();
    }
}
