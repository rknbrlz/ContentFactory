using HgermanContentFactory.Core.Enums;
using System.ComponentModel.DataAnnotations;

namespace HgermanContentFactory.Core.DTOs;

// ── Channel ────────────────────────────────────────────────────────────────

public class ChannelDto
{
    public int    Id          { get; set; }
    public string Name        { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ContentLanguage Language  { get; set; }
    public string LanguageName       => Language.ToString();
    public NicheCategory   Niche     { get; set; }
    public string NicheName          => Niche.ToString();
    public PublishPlatform Platform  { get; set; }
    public string? YouTubeChannelId  { get; set; }
    public int    DailyVideoTarget   { get; set; }
    public int    TotalVideosPublished { get; set; }
    public long   TotalViews         { get; set; }
    public long   TotalSubscribers   { get; set; }
    public bool   IsAuthenticated    => !string.IsNullOrEmpty(YouTubeChannelId);
    public bool   IsActive           { get; set; }
    public DateTime CreatedAt        { get; set; }
}

public class CreateChannelDto
{
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string Description { get; set; } = string.Empty;

    [Required]
    public ContentLanguage Language { get; set; }

    [Required]
    public NicheCategory Niche { get; set; }

    [Required]
    public PublishPlatform Platform { get; set; }

    [Range(1, 50)]
    public int DailyVideoTarget { get; set; } = 1;

    [MaxLength(500)]
    public string? DefaultHashtags { get; set; }
}

public class EditChannelDto : CreateChannelDto
{
    public int Id { get; set; }
}

// ── Video ──────────────────────────────────────────────────────────────────

public class VideoDto
{
    public int    Id           { get; set; }
    public string Title        { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Script      { get; set; }
    public VideoStatus Status  { get; set; }
    public string StatusName   => Status.ToString();
    public ContentLanguage Language { get; set; }
    public NicheCategory   Niche   { get; set; }
    public string ChannelName  { get; set; } = string.Empty;
    public int    ChannelId    { get; set; }
    public string? YouTubeVideoId  { get; set; }
    public string? YouTubeUrl      => YouTubeVideoId != null ? $"https://youtu.be/{YouTubeVideoId}" : null;
    public string? ThumbnailUrl    { get; set; }
    public DateTime? ScheduledAt   { get; set; }
    public DateTime? PublishedAt   { get; set; }
    public long   Views            { get; set; }
    public long   Likes            { get; set; }
    public long   Comments         { get; set; }
    public string? TrendTopicTitle { get; set; }
    public string? ErrorMessage    { get; set; }
    public DateTime CreatedAt      { get; set; }
    public int DurationSeconds     { get; set; }
    public string? Hashtags        { get; set; }
}

public class GenerateVideoRequestDto
{
    [Required]
    public int ChannelId { get; set; }
    public int?    TrendTopicId      { get; set; }
    public string? CustomTopic       { get; set; }
    public bool    AutoPublish       { get; set; } = false;
    public DateTime? ScheduleAt     { get; set; }
    public string? StyleInstructions { get; set; }
}

// ── Trend ──────────────────────────────────────────────────────────────────

public class TrendTopicDto
{
    public int    Id          { get; set; }
    public string Title       { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public NicheCategory   Niche    { get; set; }
    public ContentLanguage Language { get; set; }
    public double TrendScore  { get; set; }
    public TrendStatus Status { get; set; }
    public string? Keywords   { get; set; }
    public DateTime DiscoveredAt { get; set; }
    public int UsageCount     { get; set; }
}

// ── Campaign ───────────────────────────────────────────────────────────────

public class CampaignDto
{
    public int    Id                  { get; set; }
    public string Name                { get; set; } = string.Empty;
    public string? Description        { get; set; }
    public int    ChannelId           { get; set; }
    public string ChannelName         { get; set; } = string.Empty;
    public DateTime StartDate         { get; set; }
    public DateTime? EndDate          { get; set; }
    public int  VideosPerDay          { get; set; }
    public int  TotalVideosPlanned    { get; set; }
    public int  TotalVideosProduced   { get; set; }
    public bool AutoPublish           { get; set; }
    public string? ContentStyle       { get; set; }
    public string? TargetAudience     { get; set; }
    public bool IsActive              { get; set; }
    public DateTime CreatedAt         { get; set; }
}

public class CreateCampaignDto
{
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    [MaxLength(1000)]
    public string? Description { get; set; }
    [Required]
    public int ChannelId { get; set; }
    public DateTime StartDate { get; set; } = DateTime.UtcNow;
    public DateTime? EndDate { get; set; }
    [Range(1, 50)]
    public int VideosPerDay { get; set; } = 1;
    public bool AutoPublish { get; set; } = true;
    [MaxLength(500)]
    public string? ContentStyle { get; set; }
    [MaxLength(500)]
    public string? TargetAudience { get; set; }
}

// ── Dashboard ──────────────────────────────────────────────────────────────

public class DashboardStatsDto
{
    public int  TotalChannels    { get; set; }
    public int  ActiveChannels   { get; set; }
    public int  TotalVideos      { get; set; }
    public int  PublishedToday   { get; set; }
    public int  PendingVideos    { get; set; }
    public int  FailedVideos     { get; set; }
    public long TotalViews       { get; set; }
    public long TotalSubscribers { get; set; }
    public int  TrendingTopics   { get; set; }
    public int  ActiveCampaigns  { get; set; }

    public List<ChannelSummaryDto> ChannelSummaries { get; set; } = new();
    public List<VideoDto>          RecentVideos     { get; set; } = new();
    public List<TrendTopicDto>     TopTrends        { get; set; } = new();
    public List<DailyPublishDto>   WeeklyChart      { get; set; } = new();
}

public class ChannelSummaryDto
{
    public int    ChannelId     { get; set; }
    public string ChannelName   { get; set; } = string.Empty;
    public string Language      { get; set; } = string.Empty;
    public string Niche         { get; set; } = string.Empty;
    public string Platform      { get; set; } = string.Empty;
    public int    PublishedToday { get; set; }
    public int    DailyTarget   { get; set; }
    public long   Views         { get; set; }
    public long   Subscribers   { get; set; }
    public bool   IsAuthenticated { get; set; }
}

public class DailyPublishDto
{
    public string Date  { get; set; } = string.Empty;
    public int    Count { get; set; }
}

// ── YouTube Auth ───────────────────────────────────────────────────────────

public class YouTubeAuthDto
{
    public int    ChannelId         { get; set; }
    public string AuthorizationCode { get; set; } = string.Empty;
    public string RedirectUri       { get; set; } = string.Empty;
}

// ── Schedule ───────────────────────────────────────────────────────────────

public class ScheduleDto
{
    public int         ChannelId    { get; set; }
    public string      ChannelName  { get; set; } = string.Empty;
    [Range(1, 50)]
    public int         VideosPerDay { get; set; } = 1;
    public List<string> PublishTimes { get; set; } = new() { "09:00" };
    public bool        AutoPublish  { get; set; } = true;
    public bool        IsRunning    { get; set; }
    public DateTime?   LastRun      { get; set; }
    public DateTime?   NextRun      { get; set; }
}

// ── Settings ───────────────────────────────────────────────────────────────

public class ApiKeyDto
{
    public int    Id       { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string KeyName  { get; set; } = string.Empty;
    public bool   IsDefault { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public class SaveApiKeyDto
{
    [Required, MaxLength(100)]
    public string Provider { get; set; } = string.Empty;
    [Required, MaxLength(100)]
    public string KeyName  { get; set; } = string.Empty;
    [Required]
    public string ApiKey   { get; set; } = string.Empty;
    public bool IsDefault  { get; set; }
}
