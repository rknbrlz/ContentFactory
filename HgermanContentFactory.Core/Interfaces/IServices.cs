using HgermanContentFactory.Core.DTOs;
using HgermanContentFactory.Core.Entities;
using HgermanContentFactory.Core.Enums;

namespace HgermanContentFactory.Core.Interfaces;

public interface IChannelRepository
{
    Task<IEnumerable<CF_Channel>> GetAllAsync();
    Task<CF_Channel?> GetByIdAsync(int id);
    Task<CF_Channel> CreateAsync(CF_Channel channel);
    Task UpdateAsync(CF_Channel channel);
    Task SoftDeleteAsync(int id);
}

public interface IVideoRepository
{
    Task<IEnumerable<CF_Video>> GetAllAsync(int? channelId = null, int page = 1, int pageSize = 20);
    Task<CF_Video?> GetByIdAsync(int id);
    Task<CF_Video> CreateAsync(CF_Video video);
    Task UpdateAsync(CF_Video video);
    Task<IEnumerable<CF_Video>> GetByStatusAsync(VideoStatus status);
    Task<IEnumerable<CF_Video>> GetDueScheduledAsync();
    Task<int> CountPublishedTodayAsync(int channelId);
    Task<int> CountTotalAsync(int? channelId = null);
}

public interface ITrendRepository
{
    Task<IEnumerable<CF_TrendTopic>> GetActiveAsync(ContentLanguage? language = null, NicheCategory? niche = null);
    Task<CF_TrendTopic?> GetByIdAsync(int id);
    Task<CF_TrendTopic> CreateAsync(CF_TrendTopic topic);
    Task UpdateAsync(CF_TrendTopic topic);
    Task<IEnumerable<CF_TrendTopic>> GetTopAsync(int count = 10);
    Task BulkInsertAsync(IEnumerable<CF_TrendTopic> topics);
}

// ── Application Services ───────────────────────────────────────────────────

public interface IContentGenerationService
{
    Task<string> GenerateScriptAsync(string topic, ContentLanguage language, NicheCategory niche, string? style = null);
    Task<string> GenerateTitleAsync(string topic, ContentLanguage language, NicheCategory niche);
    Task<string> GenerateDescriptionAsync(string title, string script, ContentLanguage language);
    Task<string> GenerateHashtagsAsync(string topic, ContentLanguage language, NicheCategory niche);
    Task<List<TrendTopicDto>> AnalyzeTrendsAsync(ContentLanguage language, NicheCategory niche);
}

public interface IYouTubeService
{
    Task<string> GetAuthorizationUrlAsync(int channelId, string redirectUri);
    Task<bool>   ExchangeCodeForTokenAsync(int channelId, string code, string redirectUri);
    Task<string?> UploadVideoAsync(int channelId, string filePath, string title, string description, string tags, bool makePublic = true);
    Task<bool>   RefreshTokenAsync(int channelId);
    Task<ChannelSummaryDto?> GetChannelStatsAsync(int channelId);
}

public interface IVideoGenerationService
{
    Task<CF_Video> GenerateAsync(GenerateVideoRequestDto request);
    Task<bool>     RenderAsync(int videoId);
    Task<bool>     PublishAsync(int videoId);
}

public interface ITrendAnalysisService
{
    Task<List<TrendTopicDto>> GetTrendingAsync(ContentLanguage language, NicheCategory niche);
    Task RefreshAllTrendsAsync();
}

public interface IDashboardService
{
    Task<DashboardStatsDto> GetStatsAsync();
}

public interface ISchedulerService
{
    Task EnqueueDueVideosAsync();
    Task ProcessScheduledPublishAsync(int videoId);
}

// ── Turkish Drama Service ──────────────────────────────────────────────────

public interface ITurkishDramaService
{
    /// <summary>Returns trending Turkish drama topics for the given language market.</summary>
    Task<List<TurkishDramaTrendDto>> GetTrendingDramasAsync(ContentLanguage language);

    /// <summary>Generates a full short-video content package for one drama topic.</summary>
    Task<TurkishDramaContentDto> GenerateContentAsync(TurkishDramaGenerateRequest request);

    /// <summary>Generates content for all 6 languages at once (bulk).</summary>
    Task<List<TurkishDramaContentDto>> GenerateBulkAsync(string dramaSeries, string sceneOrEmotion);
}
