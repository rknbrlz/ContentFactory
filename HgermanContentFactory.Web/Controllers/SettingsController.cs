using HgermanContentFactory.Core.DTOs;
using HgermanContentFactory.Core.Entities;
using HgermanContentFactory.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace HgermanContentFactory.Web.Controllers;

public class SettingsController : Controller
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public SettingsController(AppDbContext db, IConfiguration config)
    {
        _db     = db;
        _config = config;
    }

    public async Task<IActionResult> Index()
    {
        var keys = await _db.CF_ApiKeys
            .Where(k => k.IsActive)
            .Select(k => new ApiKeyDto
            {
                Id        = k.Id,
                Provider  = k.Provider,
                KeyName   = k.KeyName,
                IsDefault = k.IsDefault,
                ExpiresAt = k.ExpiresAt
            })
            .ToListAsync();

        var schedules = await _db.CF_Schedules
            .Include(s => s.Channel)
            .Where(s => s.IsActive)
            .ToListAsync();

        ViewBag.ApiKeys   = keys;
        ViewBag.Schedules = schedules;
        return View();
    }

    // POST /Settings/SaveApiKey
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveApiKey(SaveApiKeyDto dto)
    {
        if (!ModelState.IsValid) return RedirectToAction(nameof(Index));

        // If setting as default, clear others for same provider
        if (dto.IsDefault)
        {
            var existing = await _db.CF_ApiKeys
                .Where(k => k.Provider == dto.Provider && k.IsDefault)
                .ToListAsync();
            foreach (var k in existing) k.IsDefault = false;
        }

        _db.CF_ApiKeys.Add(new CF_ApiKey
        {
            Provider     = dto.Provider,
            KeyName      = dto.KeyName,
            EncryptedKey = Encrypt(dto.ApiKey),
            IsDefault    = dto.IsDefault,
            CreatedAt    = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        TempData["Success"] = $"{dto.Provider} API key saved.";
        return RedirectToAction(nameof(Index));
    }

    // POST /Settings/DeleteApiKey/5
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteApiKey(int id)
    {
        var key = await _db.CF_ApiKeys.FindAsync(id);
        if (key != null) { key.IsActive = false; await _db.SaveChangesAsync(); }
        TempData["Success"] = "API key removed.";
        return RedirectToAction(nameof(Index));
    }

    // POST /Settings/UpdateSchedule
    [HttpPost]
    public async Task<IActionResult> UpdateSchedule([FromBody] UpdateScheduleRequest req)
    {
        var sched = await _db.CF_Schedules.FindAsync(req.ScheduleId);
        if (sched == null) return Json(new { success = false });

        sched.VideosPerDay = Math.Clamp(req.VideosPerDay, 1, 50);
        sched.PublishTimes = string.Join(",", req.PublishTimes);
        sched.UpdatedAt    = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Json(new { success = true, videosPerDay = sched.VideosPerDay });
    }

    // Very basic encryption — in production use Azure Key Vault
    private static string Encrypt(string plain)
    {
        var bytes = Encoding.UTF8.GetBytes(plain);
        return Convert.ToBase64String(bytes);
    }
}

public record UpdateScheduleRequest(int ScheduleId, int VideosPerDay, List<string> PublishTimes);
