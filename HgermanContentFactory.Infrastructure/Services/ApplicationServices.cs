using HgermanContentFactory.Core.DTOs;
using HgermanContentFactory.Core.Entities;
using HgermanContentFactory.Core.Enums;
using HgermanContentFactory.Core.Interfaces;
using HgermanContentFactory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HgermanContentFactory.Infrastructure.Services;

// ── Dashboard Service ──────────────────────────────────────────────────────

public class DashboardService : IDashboardService
{
    private readonly AppDbContext _db;
    public DashboardService(AppDbContext db) => _db = db;

    public async Task<DashboardStatsDto> GetStatsAsync()
    {
        var today = DateTime.UtcNow.Date;

        var channels = await _db.CF_Channels.Where(c => c.IsActive).ToListAsync();

        var publishedToday = await _db.CF_Videos.CountAsync(v =>
            v.Status == VideoStatus.Published &&
            v.PublishedAt != null &&
            v.PublishedAt.Value.Date == today);

        var pending = await _db.CF_Videos.CountAsync(v => v.Status == VideoStatus.Pending && v.IsActive);
        var failed = await _db.CF_Videos.CountAsync(v => v.Status == VideoStatus.Failed && v.IsActive);
        var total = await _db.CF_Videos.CountAsync(v => v.IsActive);
        var trends = await _db.CF_TrendTopics.CountAsync(t => t.IsActive && (t.Status == TrendStatus.Rising || t.Status == TrendStatus.Peak));

        var activeCampaigns = await _db.CF_Campaigns.CountAsync(c =>
            c.IsActive &&
            c.StartDate <= DateTime.UtcNow &&
            (c.EndDate == null || c.EndDate >= DateTime.UtcNow));

        var publishedTodayPerChannel = await _db.CF_Videos
            .Where(v => v.Status == VideoStatus.Published &&
                        v.PublishedAt != null &&
                        v.PublishedAt.Value.Date == today)
            .GroupBy(v => v.ChannelId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        var channelSummaries = channels.Select(c => new ChannelSummaryDto
        {
            ChannelId = c.Id,
            ChannelName = c.Name,
            Language = c.Language.ToString(),
            Niche = c.Niche.ToString(),
            Platform = c.Platform.ToString(),
            DailyTarget = c.DailyVideoTarget,
            PublishedToday = publishedTodayPerChannel.GetValueOrDefault(c.Id),
            Views = c.TotalViews,
            Subscribers = c.TotalSubscribers,
            IsAuthenticated = !string.IsNullOrEmpty(c.YouTubeChannelId)
        }).ToList();

        var recentVideos = await _db.CF_Videos
            .Include(v => v.Channel)
            .Include(v => v.TrendTopic)
            .Where(v => v.IsActive)
            .OrderByDescending(v => v.CreatedAt)
            .Take(10)
            .Select(v => new VideoDto
            {
                Id = v.Id,
                Title = v.Title,
                Status = v.Status,
                Language = v.Language,
                Niche = v.Niche,
                ChannelId = v.ChannelId,
                ChannelName = v.Channel.Name,
                YouTubeVideoId = v.YouTubeVideoId,
                ThumbnailUrl = v.ThumbnailUrl,
                ScheduledAt = v.ScheduledAt,
                PublishedAt = v.PublishedAt,
                Views = v.Views,
                Likes = v.Likes,
                TrendTopicTitle = v.TrendTopic != null ? v.TrendTopic.Title : null,
                CreatedAt = v.CreatedAt,
                DurationSeconds = v.DurationSeconds
            })
            .ToListAsync();

        var topTrends = await _db.CF_TrendTopics
            .Where(t => t.IsActive)
            .OrderByDescending(t => t.TrendScore)
            .Take(10)
            .Select(t => new TrendTopicDto
            {
                Id = t.Id,
                Title = t.Title,
                Description = t.Description,
                Niche = t.Niche,
                Language = t.Language,
                TrendScore = t.TrendScore,
                Status = t.Status,
                Keywords = t.Keywords,
                DiscoveredAt = t.DiscoveredAt,
                UsageCount = t.UsageCount
            })
            .ToListAsync();

        // Weekly chart: fetch dates from SQL, format strings in C# (ToString not translatable to SQL)
        var sevenDaysAgo = DateTime.UtcNow.AddDays(-7);
        var weeklyChartRaw = await _db.CF_Videos
            .Where(v => v.Status == VideoStatus.Published &&
                        v.PublishedAt != null &&
                        v.PublishedAt >= sevenDaysAgo)
            .GroupBy(v => v.PublishedAt!.Value.Date)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .OrderBy(x => x.Date)
            .ToListAsync();

        var weeklyChart = weeklyChartRaw
            .Select(x => new DailyPublishDto
            {
                Date = x.Date.ToString("MM/dd"),
                Count = x.Count
            })
            .ToList();

        return new DashboardStatsDto
        {
            TotalChannels = channels.Count,
            ActiveChannels = channels.Count(c => c.IsActive),
            TotalVideos = total,
            PublishedToday = publishedToday,
            PendingVideos = pending,
            FailedVideos = failed,
            TotalViews = channels.Sum(c => c.TotalViews),
            TotalSubscribers = channels.Sum(c => c.TotalSubscribers),
            TrendingTopics = trends,
            ActiveCampaigns = activeCampaigns,
            ChannelSummaries = channelSummaries,
            RecentVideos = recentVideos,
            TopTrends = topTrends,
            WeeklyChart = weeklyChart
        };
    }
}

// ── Trend Analysis Service ─────────────────────────────────────────────────

public class TrendAnalysisService : ITrendAnalysisService
{
    private readonly AppDbContext _db;
    private readonly IContentGenerationService _ai;
    private readonly ILogger<TrendAnalysisService> _logger;

    public TrendAnalysisService(AppDbContext db, IContentGenerationService ai,
        ILogger<TrendAnalysisService> logger)
    {
        _db = db;
        _ai = ai;
        _logger = logger;
    }

    public async Task<List<TrendTopicDto>> GetTrendingAsync(ContentLanguage language, NicheCategory niche)
    {
        var fresh = await _db.CF_TrendTopics
            .Where(t => t.Language == language && t.Niche == niche && t.IsActive &&
                        t.DiscoveredAt >= DateTime.UtcNow.AddHours(-6))
            .OrderByDescending(t => t.TrendScore)
            .Take(10)
            .ToListAsync();

        if (fresh.Count >= 5)
            return fresh.Select(MapToDto).ToList();

        _logger.LogInformation("Refreshing trends for {Lang}/{Niche}", language, niche);
        var newTrends = await _ai.AnalyzeTrendsAsync(language, niche);

        var entities = newTrends.Select(t => new CF_TrendTopic
        {
            Title = t.Title,
            Description = t.Description,
            Niche = niche,
            Language = language,
            TrendScore = t.TrendScore,
            Keywords = t.Keywords,
            Status = TrendStatus.Rising,
            DiscoveredAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(3)
        }).ToList();

        await _db.CF_TrendTopics.AddRangeAsync(entities);
        await _db.SaveChangesAsync();

        return entities.Select(MapToDto).ToList();
    }

    public async Task RefreshAllTrendsAsync()
    {
        var stale = await _db.CF_TrendTopics
            .Where(t => t.ExpiresAt < DateTime.UtcNow && t.IsActive)
            .ToListAsync();
        foreach (var t in stale) { t.IsActive = false; t.Status = TrendStatus.Declining; }
        await _db.SaveChangesAsync();

        var activeCombos = await _db.CF_Channels
            .Where(c => c.IsActive)
            .Select(c => new { c.Language, c.Niche })
            .Distinct()
            .ToListAsync();

        foreach (var combo in activeCombos)
        {
            try { await GetTrendingAsync(combo.Language, combo.Niche); }
            catch (Exception ex) { _logger.LogError(ex, "Trend refresh failed for {L}/{N}", combo.Language, combo.Niche); }
        }
    }

    private static TrendTopicDto MapToDto(CF_TrendTopic t) => new()
    {
        Id = t.Id,
        Title = t.Title,
        Description = t.Description,
        Niche = t.Niche,
        Language = t.Language,
        TrendScore = t.TrendScore,
        Status = t.Status,
        Keywords = t.Keywords,
        DiscoveredAt = t.DiscoveredAt,
        UsageCount = t.UsageCount
    };
}

// ── Scheduler Service ──────────────────────────────────────────────────────

public class SchedulerService : ISchedulerService
{
    private readonly AppDbContext _db;
    private readonly IVideoGenerationService _videoGen;
    private readonly ILogger<SchedulerService> _logger;

    public SchedulerService(AppDbContext db, IVideoGenerationService videoGen,
        ILogger<SchedulerService> logger)
    {
        _db = db;
        _videoGen = videoGen;
        _logger = logger;
    }

    public async Task EnqueueDueVideosAsync()
    {
        var schedules = await _db.CF_Schedules
            .Include(s => s.Channel)
            .Where(s => s.IsActive && s.Channel.IsActive && s.NextRun <= DateTime.UtcNow)
            .ToListAsync();

        foreach (var schedule in schedules)
        {
            try
            {
                var todayDate = DateTime.UtcNow.Date;
                var todayCount = await _db.CF_Videos.CountAsync(v =>
                    v.ChannelId == schedule.ChannelId &&
                    v.Status == VideoStatus.Published &&
                    v.PublishedAt != null &&
                    v.PublishedAt.Value.Date == todayDate);

                var remaining = schedule.VideosPerDay - todayCount;
                for (int i = 0; i < remaining; i++)
                {
                    await _videoGen.GenerateAsync(new GenerateVideoRequestDto
                    {
                        ChannelId = schedule.ChannelId,
                        AutoPublish = true
                    });
                }

                schedule.LastRun = DateTime.UtcNow;
                schedule.NextRun = DateTime.UtcNow.Date.AddDays(1).AddHours(9);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduler failed for channel {Id}", schedule.ChannelId);
            }
        }

        await _db.SaveChangesAsync();
    }

    public async Task ProcessScheduledPublishAsync(int videoId)
    {
        await _videoGen.RenderAsync(videoId);
        await _videoGen.PublishAsync(videoId);
    }
}