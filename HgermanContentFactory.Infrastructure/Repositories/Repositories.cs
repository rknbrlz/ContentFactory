using HgermanContentFactory.Core.Entities;
using HgermanContentFactory.Core.Enums;
using HgermanContentFactory.Core.Interfaces;
using HgermanContentFactory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HgermanContentFactory.Infrastructure.Repositories;

// ── Channel Repository ─────────────────────────────────────────────────────

public class ChannelRepository : IChannelRepository
{
    private readonly AppDbContext _db;
    public ChannelRepository(AppDbContext db) => _db = db;

    public async Task<IEnumerable<CF_Channel>> GetAllAsync() =>
        await _db.CF_Channels.Where(c => c.IsActive)
                              .OrderBy(c => c.Language).ThenBy(c => c.Niche)
                              .ToListAsync();

    public async Task<CF_Channel?> GetByIdAsync(int id) =>
        await _db.CF_Channels.FirstOrDefaultAsync(c => c.Id == id && c.IsActive);

    public async Task<CF_Channel> CreateAsync(CF_Channel channel)
    {
        _db.CF_Channels.Add(channel);
        await _db.SaveChangesAsync();
        return channel;
    }

    public async Task UpdateAsync(CF_Channel channel)
    {
        channel.UpdatedAt = DateTime.UtcNow;
        _db.CF_Channels.Update(channel);
        await _db.SaveChangesAsync();
    }

    public async Task SoftDeleteAsync(int id)
    {
        var ch = await _db.CF_Channels.FindAsync(id);
        if (ch != null) { ch.IsActive = false; ch.UpdatedAt = DateTime.UtcNow; }
        await _db.SaveChangesAsync();
    }
}

// ── Video Repository ───────────────────────────────────────────────────────

public class VideoRepository : IVideoRepository
{
    private readonly AppDbContext _db;
    public VideoRepository(AppDbContext db) => _db = db;

    public async Task<IEnumerable<CF_Video>> GetAllAsync(int? channelId = null, int page = 1, int pageSize = 20)
    {
        var q = _db.CF_Videos.Include(v => v.Channel)
                              .Include(v => v.TrendTopic)
                              .Where(v => v.IsActive);
        if (channelId.HasValue) q = q.Where(v => v.ChannelId == channelId.Value);
        return await q.OrderByDescending(v => v.CreatedAt)
                      .Skip((page - 1) * pageSize).Take(pageSize)
                      .ToListAsync();
    }

    public async Task<CF_Video?> GetByIdAsync(int id) =>
        await _db.CF_Videos.Include(v => v.Channel)
                            .Include(v => v.TrendTopic)
                            .FirstOrDefaultAsync(v => v.Id == id && v.IsActive);

    public async Task<CF_Video> CreateAsync(CF_Video video)
    {
        _db.CF_Videos.Add(video);
        await _db.SaveChangesAsync();
        return video;
    }

    public async Task UpdateAsync(CF_Video video)
    {
        video.UpdatedAt = DateTime.UtcNow;
        _db.CF_Videos.Update(video);
        await _db.SaveChangesAsync();
    }

    public async Task<IEnumerable<CF_Video>> GetByStatusAsync(VideoStatus status) =>
        await _db.CF_Videos.Include(v => v.Channel)
                            .Where(v => v.Status == status && v.IsActive)
                            .OrderBy(v => v.CreatedAt)
                            .ToListAsync();

    public async Task<IEnumerable<CF_Video>> GetDueScheduledAsync() =>
        await _db.CF_Videos.Include(v => v.Channel)
                            .Where(v => v.Status == VideoStatus.Scheduled
                                     && v.ScheduledAt <= DateTime.UtcNow
                                     && v.IsActive)
                            .OrderBy(v => v.ScheduledAt)
                            .ToListAsync();

    public async Task<int> CountPublishedTodayAsync(int channelId)
    {
        var today = DateTime.UtcNow.Date;
        return await _db.CF_Videos.CountAsync(v =>
            v.ChannelId == channelId &&
            v.Status == VideoStatus.Published &&
            v.PublishedAt != null &&
            v.PublishedAt.Value.Date == today);
    }

    public async Task<int> CountTotalAsync(int? channelId = null)
    {
        var q = _db.CF_Videos.Where(v => v.IsActive);
        if (channelId.HasValue) q = q.Where(v => v.ChannelId == channelId.Value);
        return await q.CountAsync();
    }
}

// ── Trend Repository ───────────────────────────────────────────────────────

public class TrendRepository : ITrendRepository
{
    private readonly AppDbContext _db;
    public TrendRepository(AppDbContext db) => _db = db;

    public async Task<IEnumerable<CF_TrendTopic>> GetActiveAsync(ContentLanguage? language = null, NicheCategory? niche = null)
    {
        var q = _db.CF_TrendTopics.Where(t => t.IsActive);
        if (language.HasValue) q = q.Where(t => t.Language == language.Value);
        if (niche.HasValue)    q = q.Where(t => t.Niche    == niche.Value);
        return await q.OrderByDescending(t => t.TrendScore).ToListAsync();
    }

    public async Task<CF_TrendTopic?> GetByIdAsync(int id) =>
        await _db.CF_TrendTopics.FirstOrDefaultAsync(t => t.Id == id && t.IsActive);

    public async Task<CF_TrendTopic> CreateAsync(CF_TrendTopic topic)
    {
        _db.CF_TrendTopics.Add(topic);
        await _db.SaveChangesAsync();
        return topic;
    }

    public async Task UpdateAsync(CF_TrendTopic topic)
    {
        topic.UpdatedAt = DateTime.UtcNow;
        _db.CF_TrendTopics.Update(topic);
        await _db.SaveChangesAsync();
    }

    public async Task<IEnumerable<CF_TrendTopic>> GetTopAsync(int count = 10) =>
        await _db.CF_TrendTopics.Where(t => t.IsActive)
                                 .OrderByDescending(t => t.TrendScore)
                                 .Take(count)
                                 .ToListAsync();

    public async Task BulkInsertAsync(IEnumerable<CF_TrendTopic> topics)
    {
        await _db.CF_TrendTopics.AddRangeAsync(topics);
        await _db.SaveChangesAsync();
    }
}
