using HgermanContentFactory.Core.DTOs;
using HgermanContentFactory.Core.Entities;
using HgermanContentFactory.Core.Enums;
using HgermanContentFactory.Core.Interfaces;
using HgermanContentFactory.Infrastructure.Data;
using HgermanContentFactory.Infrastructure.Services.Renderer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HgermanContentFactory.Infrastructure.Services;

public class VideoGenerationService : IVideoGenerationService
{
    private readonly AppDbContext _db;
    private readonly IContentGenerationService _content;
    private readonly IYouTubeService _youtube;
    private readonly VideoRenderOrchestrator _renderer;
    private readonly ILogger<VideoGenerationService> _logger;

    public VideoGenerationService(AppDbContext db, IContentGenerationService content,
        IYouTubeService youtube, VideoRenderOrchestrator renderer,
        ILogger<VideoGenerationService> logger)
    {
        _db = db;
        _content = content;
        _youtube = youtube;
        _renderer = renderer;
        _logger = logger;
    }

    // ── Generate ───────────────────────────────────────────────────────────

    public async Task<CF_Video> GenerateAsync(GenerateVideoRequestDto req)
    {
        var channel = await _db.CF_Channels.FindAsync(req.ChannelId)
            ?? throw new ArgumentException($"Channel {req.ChannelId} not found");

        // Resolve topic
        CF_TrendTopic? trend = null;
        string topic;

        if (req.TrendTopicId.HasValue)
        {
            trend = await _db.CF_TrendTopics.FindAsync(req.TrendTopicId.Value);
            topic = trend?.Title ?? req.CustomTopic ?? "Trending";
        }
        else if (!string.IsNullOrWhiteSpace(req.CustomTopic))
        {
            topic = req.CustomTopic;
        }
        else
        {
            // Pick highest-scoring trend for this channel's language + niche
            trend = await _db.CF_TrendTopics
                .Where(t => t.Language == channel.Language && t.Niche == channel.Niche && t.IsActive)
                .OrderByDescending(t => t.TrendScore)
                .FirstOrDefaultAsync();
            topic = trend?.Title ?? $"{channel.Niche} tips";
        }

        _logger.LogInformation("Generating video — channel: {Channel}, topic: {Topic}", channel.Name, topic);

        // Parallel AI calls
        var titleTask = _content.GenerateTitleAsync(topic, channel.Language, channel.Niche);
        await titleTask; // title needed for description
        var title = titleTask.Result;

        var (script, description, hashtags) = await (
            _content.GenerateScriptAsync(topic, channel.Language, channel.Niche, req.StyleInstructions),
            _content.GenerateDescriptionAsync(title, topic, channel.Language),
            _content.GenerateHashtagsAsync(topic, channel.Language, channel.Niche)
        ).WhenAll();

        if (!string.IsNullOrEmpty(channel.DefaultHashtags))
            hashtags += " " + channel.DefaultHashtags;

        var video = new CF_Video
        {
            Title = title,
            Description = description,
            Script = script,
            Hashtags = hashtags.Trim(),
            Status = req.ScheduleAt.HasValue ? VideoStatus.Scheduled : VideoStatus.ScriptReady,
            Language = channel.Language,
            Niche = channel.Niche,
            ChannelId = channel.Id,
            TrendTopicId = trend?.Id,
            ScheduledAt = req.ScheduleAt,
            AIPromptUsed = $"topic={topic}|lang={channel.Language}|niche={channel.Niche}",
            CreatedAt = DateTime.UtcNow
        };

        _db.CF_Videos.Add(video);
        await _db.SaveChangesAsync();

        if (trend != null) { trend.UsageCount++; await _db.SaveChangesAsync(); }

        _logger.LogInformation("Video {Id} created: {Title}", video.Id, video.Title);

        if (req.AutoPublish && !req.ScheduleAt.HasValue)
        {
            await RenderAsync(video.Id);
            await PublishAsync(video.Id);
        }

        return video;
    }

    // ── Render ─────────────────────────────────────────────────────────────
    // Full pipeline: TTS (ElevenLabs) → Images (DALL-E 3) → Video (FFmpeg)

    public async Task<bool> RenderAsync(int videoId)
    {
        return await _renderer.RenderVideoAsync(videoId);
    }

    // ── Publish ────────────────────────────────────────────────────────────

    public async Task<bool> PublishAsync(int videoId)
    {
        var v = await _db.CF_Videos.Include(x => x.Channel)
                                    .FirstOrDefaultAsync(x => x.Id == videoId);
        if (v == null) return false;

        // Guard: needs a rendered video file
        if (string.IsNullOrEmpty(v.VideoFilePath) || !File.Exists(v.VideoFilePath))
        {
            _logger.LogWarning("Video {Id} has no file — cannot publish", videoId);
            v.ErrorMessage = "Video file not found";
            v.Status = VideoStatus.Failed;
            await _db.SaveChangesAsync();
            return false;
        }

        try
        {
            var ytId = await _youtube.UploadVideoAsync(
                v.ChannelId,
                v.VideoFilePath,
                v.Title,
                $"{v.Description}\n\n{v.Hashtags}",
                v.Hashtags ?? string.Empty);

            if (ytId != null)
            {
                v.YouTubeVideoId = ytId;
                v.Status = VideoStatus.Published;
                v.PublishedAt = DateTime.UtcNow;
                v.UpdatedAt = DateTime.UtcNow;
                v.Channel.TotalVideosPublished++;
                v.Channel.UpdatedAt = DateTime.UtcNow;

                _db.CF_PublishLogs.Add(new CF_PublishLog
                {
                    VideoId = videoId,
                    Platform = PublishPlatform.YouTube,
                    Success = true,
                    PlatformVideoId = ytId,
                    PlatformUrl = $"https://youtu.be/{ytId}",
                    AttemptedAt = DateTime.UtcNow
                });

                await _db.SaveChangesAsync();
                _logger.LogInformation("Video {Id} published → {YtId}", videoId, ytId);
                return true;
            }

            v.Status = VideoStatus.Failed;
            v.ErrorMessage = "Upload returned no video ID";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Publish failed for video {Id}", videoId);
            v.Status = VideoStatus.Failed;
            v.ErrorMessage = ex.Message;

            _db.CF_PublishLogs.Add(new CF_PublishLog
            {
                VideoId = videoId,
                Platform = PublishPlatform.YouTube,
                Success = false,
                ErrorMessage = ex.Message,
                AttemptedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();
        return false;
    }
}

// ── Tuple deconstruct helper ────────────────────────────────────────────────

file static class TaskExtensions
{
    public static async Task<(T1, T2, T3)> WhenAll<T1, T2, T3>(this (Task<T1> t1, Task<T2> t2, Task<T3> t3) tasks)
    {
        await Task.WhenAll(tasks.t1, tasks.t2, tasks.t3);
        return (tasks.t1.Result, tasks.t2.Result, tasks.t3.Result);
    }
}