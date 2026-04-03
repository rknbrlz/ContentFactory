using HgermanContentFactory.Core.Enums;

namespace HgermanContentFactory.Core.Entities;

public abstract class BaseEntity
{
    public int      Id        { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool     IsActive  { get; set; } = true;
}

/// <summary>CF_Channels — one row per YouTube/Instagram/TikTok channel</summary>
public class CF_Channel : BaseEntity
{
    public string Name                  { get; set; } = string.Empty;
    public string Description           { get; set; } = string.Empty;
    public ContentLanguage Language     { get; set; }
    public NicheCategory   Niche        { get; set; }
    public PublishPlatform Platform     { get; set; }
    public string? YouTubeChannelId     { get; set; }
    public string? YouTubeAccessToken   { get; set; }
    public string? YouTubeRefreshToken  { get; set; }
    public DateTime? TokenExpiry        { get; set; }
    public int DailyVideoTarget         { get; set; } = 1;
    public string? ThumbnailStyle       { get; set; }
    public string? DefaultHashtags      { get; set; }
    public int  TotalVideosPublished    { get; set; }
    public long TotalViews              { get; set; }
    public long TotalSubscribers        { get; set; }

    public ICollection<CF_Video>            Videos    { get; set; } = new List<CF_Video>();
    public ICollection<CF_Campaign>         Campaigns { get; set; } = new List<CF_Campaign>();
    public ICollection<CF_Schedule>         Schedules { get; set; } = new List<CF_Schedule>();
    public ICollection<CF_AnalyticsSnapshot> Analytics { get; set; } = new List<CF_AnalyticsSnapshot>();
}

/// <summary>CF_TrendTopics — AI-discovered trending topics per language + niche</summary>
public class CF_TrendTopic : BaseEntity
{
    public string Title                { get; set; } = string.Empty;
    public string Description         { get; set; } = string.Empty;
    public NicheCategory   Niche      { get; set; }
    public ContentLanguage Language   { get; set; }
    public double TrendScore          { get; set; }
    public TrendStatus Status         { get; set; }
    public string? Keywords           { get; set; }
    public string? SourceUrls         { get; set; }
    public DateTime DiscoveredAt      { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt        { get; set; }
    public int UsageCount             { get; set; }

    public ICollection<CF_Video> Videos { get; set; } = new List<CF_Video>();
}

/// <summary>CF_Videos — every generated short video</summary>
public class CF_Video : BaseEntity
{
    public string Title             { get; set; } = string.Empty;
    public string? Description      { get; set; }
    public string? Script           { get; set; }
    public string? Hashtags         { get; set; }
    public VideoStatus Status       { get; set; } = VideoStatus.Pending;
    public ContentLanguage Language { get; set; }
    public NicheCategory   Niche    { get; set; }
    public int ChannelId            { get; set; }
    public CF_Channel Channel       { get; set; } = null!;
    public int? TrendTopicId        { get; set; }
    public CF_TrendTopic? TrendTopic { get; set; }
    public int? CampaignId          { get; set; }
    public CF_Campaign? Campaign    { get; set; }
    public string? YouTubeVideoId   { get; set; }
    public string? ThumbnailUrl     { get; set; }
    public string? VideoFilePath    { get; set; }
    public string? AudioFilePath    { get; set; }
    public DateTime? ScheduledAt    { get; set; }
    public DateTime? PublishedAt    { get; set; }
    public long Views               { get; set; }
    public long Likes               { get; set; }
    public long Comments            { get; set; }
    public string? ErrorMessage     { get; set; }
    public int DurationSeconds      { get; set; }
    public string? AIPromptUsed     { get; set; }
}

/// <summary>CF_Campaigns — batches of videos with shared settings</summary>
public class CF_Campaign : BaseEntity
{
    public string Name              { get; set; } = string.Empty;
    public string? Description      { get; set; }
    public int ChannelId            { get; set; }
    public CF_Channel Channel       { get; set; } = null!;
    public DateTime StartDate       { get; set; }
    public DateTime? EndDate        { get; set; }
    public int VideosPerDay         { get; set; } = 1;
    public int TotalVideosPlanned   { get; set; }
    public int TotalVideosProduced  { get; set; }
    public bool AutoPublish         { get; set; } = true;
    public string? ContentStyle     { get; set; }
    public string? TargetAudience   { get; set; }

    public ICollection<CF_Video> Videos { get; set; } = new List<CF_Video>();
}

/// <summary>CF_Schedules — daily/weekly publishing schedule per channel</summary>
public class CF_Schedule : BaseEntity
{
    public int ChannelId              { get; set; }
    public CF_Channel Channel         { get; set; } = null!;
    public ScheduleFrequency Frequency { get; set; } = ScheduleFrequency.Daily;
    public int VideosPerDay           { get; set; } = 1;
    public string PublishTimes        { get; set; } = "09:00";   // comma-separated HH:mm
    public bool IsRunning             { get; set; }
    public DateTime? LastRun          { get; set; }
    public DateTime? NextRun          { get; set; }
}

/// <summary>CF_ApiKeys — encrypted third-party API keys</summary>
public class CF_ApiKey : BaseEntity
{
    public string Provider     { get; set; } = string.Empty;   // OpenAI | ElevenLabs | etc.
    public string KeyName      { get; set; } = string.Empty;
    public string EncryptedKey { get; set; } = string.Empty;
    public DateTime? ExpiresAt { get; set; }
    public bool IsDefault      { get; set; }
}

/// <summary>CF_PublishLogs — audit log for every publish attempt</summary>
public class CF_PublishLog : BaseEntity
{
    public int VideoId              { get; set; }
    public CF_Video Video           { get; set; } = null!;
    public PublishPlatform Platform { get; set; }
    public bool Success             { get; set; }
    public string? PlatformVideoId  { get; set; }
    public string? PlatformUrl      { get; set; }
    public string? ErrorMessage     { get; set; }
    public DateTime AttemptedAt     { get; set; } = DateTime.UtcNow;
}

/// <summary>CF_AnalyticsSnapshots — daily channel stats snapshot</summary>
public class CF_AnalyticsSnapshot : BaseEntity
{
    public int ChannelId          { get; set; }
    public CF_Channel Channel     { get; set; } = null!;
    public DateTime SnapshotDate  { get; set; }
    public long Views             { get; set; }
    public long Subscribers       { get; set; }
    public long Likes             { get; set; }
    public int VideosPublished    { get; set; }
    public double EngagementRate  { get; set; }
}
