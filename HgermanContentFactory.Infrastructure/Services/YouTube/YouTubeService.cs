using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.YouTube.v3.Data;
using HgermanContentFactory.Core.DTOs;
using HgermanContentFactory.Core.Interfaces;
using HgermanContentFactory.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HgermanContentFactory.Infrastructure.Services.YouTube;

public class YouTubeService : IYouTubeService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<YouTubeService> _logger;

    // OAuth scope strings
    private const string ScopeUpload = "https://www.googleapis.com/auth/youtube.upload";
    private const string ScopeForceSsl = "https://www.googleapis.com/auth/youtube.force-ssl";

    // Space-separated scopes for the auth code request (req.Scope expects a single string)
    private const string ScopesString = ScopeUpload + " " + ScopeForceSsl;

    private string ClientId => _config["YouTube:ClientId"]
        ?? throw new InvalidOperationException("YouTube:ClientId not configured");
    private string ClientSecret => _config["YouTube:ClientSecret"]
        ?? throw new InvalidOperationException("YouTube:ClientSecret not configured");

    public YouTubeService(AppDbContext db, IConfiguration config,
        ILogger<YouTubeService> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    // ── Public API ─────────────────────────────────────────────────────────

    public async Task<string> GetAuthorizationUrlAsync(int channelId, string redirectUri)
    {
        var flow = BuildFlow();
        var req = flow.CreateAuthorizationCodeRequest(redirectUri);
        req.Scope = ScopesString;   // string, not string[]
        req.State = channelId.ToString();
        return req.Build().ToString();
    }

    public async Task<bool> ExchangeCodeForTokenAsync(int channelId, string code,
        string redirectUri)
    {
        try
        {
            var flow = BuildFlow();
            var token = await flow.ExchangeCodeForTokenAsync(
                "user", code, redirectUri, CancellationToken.None);

            return await SaveTokenAsync(channelId,
                token.AccessToken,
                token.RefreshToken,
                token.IssuedUtc.AddSeconds(token.ExpiresInSeconds ?? 3600));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Token exchange failed for channel {Id}", channelId);
            return false;
        }
    }

    public async Task<bool> RefreshTokenAsync(int channelId)
    {
        try
        {
            var ch = await _db.CF_Channels.FindAsync(channelId);
            if (ch?.YouTubeRefreshToken == null) return false;

            var flow = BuildFlow();
            var newToken = await flow.RefreshTokenAsync(
                "user", ch.YouTubeRefreshToken, CancellationToken.None);

            return await SaveTokenAsync(channelId,
                newToken.AccessToken,
                ch.YouTubeRefreshToken,
                DateTime.UtcNow.AddSeconds(newToken.ExpiresInSeconds ?? 3600));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Token refresh failed for channel {Id}", channelId);
            return false;
        }
    }

    public async Task<string?> UploadVideoAsync(int channelId, string filePath,
        string title, string description, string tags, bool makePublic = true)
    {
        try
        {
            var ch = await _db.CF_Channels.FindAsync(channelId);
            if (ch?.YouTubeAccessToken == null)
            {
                _logger.LogWarning("Channel {Id} has no YouTube token", channelId);
                return null;
            }

            if (ch.TokenExpiry < DateTime.UtcNow)
                await RefreshTokenAsync(channelId);

            var credential = GoogleCredential
                .FromAccessToken(ch.YouTubeAccessToken)
                .CreateScoped(ScopeUpload);

            // FIX: Use FULL namespace to avoid conflict with this class name
            var ytService = new Google.Apis.YouTube.v3.YouTubeService(
                new BaseClientService.Initializer
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "HgermanContentFactory"
                });

            var video = new Video
            {
                Snippet = new VideoSnippet
                {
                    Title = title,
                    Description = description,
                    Tags = tags
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(t => t.Trim())
                        .ToList(),
                    CategoryId = "22"  // People & Blogs
                },
                Status = new VideoStatus
                {
                    PrivacyStatus = makePublic ? "public" : "private",
                    SelfDeclaredMadeForKids = false
                }
            };

            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            var insert = ytService.Videos.Insert(video, "snippet,status", stream, "video/*");
            insert.ProgressChanged += p =>
                _logger.LogDebug("Upload progress: {Bytes} bytes", p.BytesSent);

            var result = await insert.UploadAsync();

            if (result.Status == UploadStatus.Completed)
            {
                _logger.LogInformation("YouTube upload complete: {Id}", insert.ResponseBody?.Id);
                return insert.ResponseBody?.Id;
            }

            _logger.LogError("YouTube upload failed: {Msg}", result.Exception?.Message);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "YouTube upload exception for channel {Id}", channelId);
            return null;
        }
    }

    public async Task<ChannelSummaryDto?> GetChannelStatsAsync(int channelId)
    {
        var ch = await _db.CF_Channels.FindAsync(channelId);
        if (ch == null) return null;
        return new ChannelSummaryDto
        {
            ChannelId = channelId,
            ChannelName = ch.Name,
            Subscribers = ch.TotalSubscribers,
            Views = ch.TotalViews
        };
    }

    // ── Private ────────────────────────────────────────────────────────────

    private GoogleAuthorizationCodeFlow BuildFlow()
    {
        return new GoogleAuthorizationCodeFlow(
            new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = ClientId,
                    ClientSecret = ClientSecret
                },
                Scopes = new[] { ScopeUpload, ScopeForceSsl }  // IEnumerable<string>
            });
    }

    private async Task<bool> SaveTokenAsync(int channelId, string accessToken,
        string? refreshToken, DateTime expiry)
    {
        var ch = await _db.CF_Channels.FindAsync(channelId);
        if (ch == null) return false;
        ch.YouTubeAccessToken = accessToken;
        if (refreshToken != null) ch.YouTubeRefreshToken = refreshToken;
        ch.TokenExpiry = expiry;
        ch.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }
}