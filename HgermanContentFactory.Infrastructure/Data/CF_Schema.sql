-- ====================================================================
--  HgermanAppsDB  —  Content Factory Schema
--  All objects prefixed with CF_
--  Target: Azure SQL Database (SQL Server 2019+)
--  Run this script once to create all tables, indexes, views and SPs
-- ====================================================================

USE HgermanAppsDB;
GO

-- ── TABLES ─────────────────────────────────────────────────────────────────

CREATE TABLE CF_Channels (
    Id                   INT           IDENTITY(1,1) PRIMARY KEY,
    Name                 NVARCHAR(200) NOT NULL,
    Description          NVARCHAR(1000) NULL,
    Language             INT           NOT NULL,          -- ContentLanguage enum
    Niche                INT           NOT NULL,          -- NicheCategory enum
    Platform             INT           NOT NULL,          -- PublishPlatform enum
    YouTubeChannelId     NVARCHAR(100) NULL,
    YouTubeAccessToken   NVARCHAR(2000) NULL,
    YouTubeRefreshToken  NVARCHAR(2000) NULL,
    TokenExpiry          DATETIME2     NULL,
    DailyVideoTarget     INT           NOT NULL DEFAULT 1,
    ThumbnailStyle       NVARCHAR(200) NULL,
    DefaultHashtags      NVARCHAR(500) NULL,
    TotalVideosPublished INT           NOT NULL DEFAULT 0,
    TotalViews           BIGINT        NOT NULL DEFAULT 0,
    TotalSubscribers     BIGINT        NOT NULL DEFAULT 0,
    IsActive             BIT           NOT NULL DEFAULT 1,
    CreatedAt            DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt            DATETIME2     NULL
);
GO

CREATE TABLE CF_TrendTopics (
    Id           INT            IDENTITY(1,1) PRIMARY KEY,
    Title        NVARCHAR(300)  NOT NULL,
    Description  NVARCHAR(2000) NULL,
    Niche        INT            NOT NULL,
    Language     INT            NOT NULL,
    TrendScore   FLOAT          NOT NULL DEFAULT 0,
    Status       INT            NOT NULL DEFAULT 1,    -- TrendStatus enum
    Keywords     NVARCHAR(500)  NULL,
    SourceUrls   NVARCHAR(2000) NULL,
    DiscoveredAt DATETIME2      NOT NULL DEFAULT GETUTCDATE(),
    ExpiresAt    DATETIME2      NULL,
    UsageCount   INT            NOT NULL DEFAULT 0,
    IsActive     BIT            NOT NULL DEFAULT 1,
    CreatedAt    DATETIME2      NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt    DATETIME2      NULL
);
GO

CREATE INDEX IX_CF_TrendTopics_LangNicheScore
    ON CF_TrendTopics (Language, Niche, TrendScore DESC)
    WHERE IsActive = 1;
GO

CREATE TABLE CF_Campaigns (
    Id                  INT            IDENTITY(1,1) PRIMARY KEY,
    Name                NVARCHAR(200)  NOT NULL,
    Description         NVARCHAR(1000) NULL,
    ChannelId           INT            NOT NULL REFERENCES CF_Channels(Id),
    StartDate           DATETIME2      NOT NULL,
    EndDate             DATETIME2      NULL,
    VideosPerDay        INT            NOT NULL DEFAULT 1,
    TotalVideosPlanned  INT            NOT NULL DEFAULT 0,
    TotalVideosProduced INT            NOT NULL DEFAULT 0,
    AutoPublish         BIT            NOT NULL DEFAULT 1,
    ContentStyle        NVARCHAR(500)  NULL,
    TargetAudience      NVARCHAR(500)  NULL,
    IsActive            BIT            NOT NULL DEFAULT 1,
    CreatedAt           DATETIME2      NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt           DATETIME2      NULL
);
GO

CREATE TABLE CF_Videos (
    Id              INT             IDENTITY(1,1) PRIMARY KEY,
    Title           NVARCHAR(500)   NOT NULL,
    Description     NVARCHAR(5000)  NULL,
    Script          NVARCHAR(MAX)   NULL,
    Hashtags        NVARCHAR(1000)  NULL,
    Status          INT             NOT NULL DEFAULT 0,   -- VideoStatus enum
    Language        INT             NOT NULL,
    Niche           INT             NOT NULL,
    ChannelId       INT             NOT NULL REFERENCES CF_Channels(Id),
    TrendTopicId    INT             NULL     REFERENCES CF_TrendTopics(Id),
    CampaignId      INT             NULL     REFERENCES CF_Campaigns(Id),
    YouTubeVideoId  NVARCHAR(50)    NULL,
    ThumbnailUrl    NVARCHAR(500)   NULL,
    VideoFilePath   NVARCHAR(500)   NULL,
    AudioFilePath   NVARCHAR(500)   NULL,
    ScheduledAt     DATETIME2       NULL,
    PublishedAt     DATETIME2       NULL,
    Views           BIGINT          NOT NULL DEFAULT 0,
    Likes           BIGINT          NOT NULL DEFAULT 0,
    Comments        BIGINT          NOT NULL DEFAULT 0,
    ErrorMessage    NVARCHAR(2000)  NULL,
    DurationSeconds INT             NOT NULL DEFAULT 0,
    AIPromptUsed    NVARCHAR(2000)  NULL,
    IsActive        BIT             NOT NULL DEFAULT 1,
    CreatedAt       DATETIME2       NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt       DATETIME2       NULL
);
GO

CREATE INDEX IX_CF_Videos_Status        ON CF_Videos (Status)            WHERE IsActive = 1;
CREATE INDEX IX_CF_Videos_ScheduledAt   ON CF_Videos (ScheduledAt)       WHERE IsActive = 1;
CREATE INDEX IX_CF_Videos_ChannelStatus ON CF_Videos (ChannelId, Status) WHERE IsActive = 1;
GO

CREATE TABLE CF_Schedules (
    Id           INT           IDENTITY(1,1) PRIMARY KEY,
    ChannelId    INT           NOT NULL REFERENCES CF_Channels(Id) ON DELETE CASCADE,
    Frequency    INT           NOT NULL DEFAULT 1,
    VideosPerDay INT           NOT NULL DEFAULT 1,
    PublishTimes NVARCHAR(200) NOT NULL DEFAULT '09:00',
    IsRunning    BIT           NOT NULL DEFAULT 0,
    LastRun      DATETIME2     NULL,
    NextRun      DATETIME2     NULL,
    IsActive     BIT           NOT NULL DEFAULT 1,
    CreatedAt    DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt    DATETIME2     NULL
);
GO

CREATE TABLE CF_ApiKeys (
    Id           INT            IDENTITY(1,1) PRIMARY KEY,
    Provider     NVARCHAR(100)  NOT NULL,
    KeyName      NVARCHAR(100)  NOT NULL,
    EncryptedKey NVARCHAR(2000) NOT NULL,
    ExpiresAt    DATETIME2      NULL,
    IsDefault    BIT            NOT NULL DEFAULT 0,
    IsActive     BIT            NOT NULL DEFAULT 1,
    CreatedAt    DATETIME2      NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt    DATETIME2      NULL
);
GO

CREATE TABLE CF_PublishLogs (
    Id              INT            IDENTITY(1,1) PRIMARY KEY,
    VideoId         INT            NOT NULL REFERENCES CF_Videos(Id) ON DELETE CASCADE,
    Platform        INT            NOT NULL,
    Success         BIT            NOT NULL DEFAULT 0,
    PlatformVideoId NVARCHAR(100)  NULL,
    PlatformUrl     NVARCHAR(500)  NULL,
    ErrorMessage    NVARCHAR(2000) NULL,
    AttemptedAt     DATETIME2      NOT NULL DEFAULT GETUTCDATE(),
    IsActive        BIT            NOT NULL DEFAULT 1,
    CreatedAt       DATETIME2      NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt       DATETIME2      NULL
);
GO

CREATE TABLE CF_AnalyticsSnapshots (
    Id              INT       IDENTITY(1,1) PRIMARY KEY,
    ChannelId       INT       NOT NULL REFERENCES CF_Channels(Id) ON DELETE CASCADE,
    SnapshotDate    DATE      NOT NULL,
    Views           BIGINT    NOT NULL DEFAULT 0,
    Subscribers     BIGINT    NOT NULL DEFAULT 0,
    Likes           BIGINT    NOT NULL DEFAULT 0,
    VideosPublished INT       NOT NULL DEFAULT 0,
    EngagementRate  FLOAT     NOT NULL DEFAULT 0,
    IsActive        BIT       NOT NULL DEFAULT 1,
    CreatedAt       DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt       DATETIME2 NULL
);
GO

CREATE INDEX IX_CF_Analytics_ChannelDate
    ON CF_AnalyticsSnapshots (ChannelId, SnapshotDate DESC);
GO

-- ── VIEWS ──────────────────────────────────────────────────────────────────

-- CF_V_ChannelSummary: Aggregated stats per channel
CREATE OR ALTER VIEW CF_V_ChannelSummary AS
SELECT
    c.Id,
    c.Name,
    c.Language,
    c.Niche,
    c.Platform,
    c.DailyVideoTarget,
    c.TotalVideosPublished,
    c.TotalViews,
    c.TotalSubscribers,
    c.YouTubeChannelId,
    c.IsActive,
    COUNT(v.Id)                                                           AS TotalVideos,
    SUM(CASE WHEN v.Status = 4
              AND CAST(v.PublishedAt AS DATE) = CAST(GETUTCDATE() AS DATE)
              THEN 1 ELSE 0 END)                                          AS PublishedToday,
    SUM(CASE WHEN v.Status = 0 THEN 1 ELSE 0 END)                        AS PendingVideos,
    SUM(CASE WHEN v.Status = 5 THEN 1 ELSE 0 END)                        AS FailedVideos,
    SUM(CASE WHEN v.Status = 6 THEN 1 ELSE 0 END)                        AS ScheduledVideos
FROM CF_Channels c
LEFT JOIN CF_Videos v ON v.ChannelId = c.Id AND v.IsActive = 1
GROUP BY c.Id, c.Name, c.Language, c.Niche, c.Platform,
         c.DailyVideoTarget, c.TotalVideosPublished,
         c.TotalViews, c.TotalSubscribers, c.YouTubeChannelId, c.IsActive;
GO

-- CF_V_DashboardStats: Single-row dashboard KPIs
CREATE OR ALTER VIEW CF_V_DashboardStats AS
SELECT
    (SELECT COUNT(*)   FROM CF_Channels     WHERE IsActive = 1)           AS TotalChannels,
    (SELECT COUNT(*)   FROM CF_Videos       WHERE IsActive = 1)           AS TotalVideos,
    (SELECT COUNT(*)   FROM CF_Videos
     WHERE Status = 4
       AND CAST(PublishedAt AS DATE) = CAST(GETUTCDATE() AS DATE))        AS PublishedToday,
    (SELECT COUNT(*)   FROM CF_Videos       WHERE Status = 0 AND IsActive = 1) AS PendingVideos,
    (SELECT COUNT(*)   FROM CF_Videos       WHERE Status = 5 AND IsActive = 1) AS FailedVideos,
    (SELECT ISNULL(SUM(TotalViews), 0)
     FROM CF_Channels WHERE IsActive = 1)                                 AS TotalViews,
    (SELECT ISNULL(SUM(TotalSubscribers), 0)
     FROM CF_Channels WHERE IsActive = 1)                                 AS TotalSubscribers,
    (SELECT COUNT(*)   FROM CF_TrendTopics
     WHERE Status IN (1, 2) AND IsActive = 1)                            AS ActiveTrends,
    (SELECT COUNT(*)   FROM CF_Campaigns
     WHERE IsActive = 1
       AND StartDate <= GETUTCDATE()
       AND (EndDate IS NULL OR EndDate >= GETUTCDATE()))                  AS ActiveCampaigns;
GO

-- CF_V_VideoDetails: Videos with related channel and trend info
CREATE OR ALTER VIEW CF_V_VideoDetails AS
SELECT
    v.Id,
    v.Title,
    v.Description,
    v.Status,
    v.Language,
    v.Niche,
    v.ChannelId,
    c.Name          AS ChannelName,
    c.Platform      AS ChannelPlatform,
    v.YouTubeVideoId,
    v.ThumbnailUrl,
    v.ScheduledAt,
    v.PublishedAt,
    v.Views,
    v.Likes,
    v.Comments,
    v.DurationSeconds,
    v.ErrorMessage,
    t.Title         AS TrendTopicTitle,
    t.TrendScore,
    v.CampaignId,
    v.CreatedAt
FROM CF_Videos v
INNER JOIN CF_Channels    c ON c.Id = v.ChannelId
LEFT  JOIN CF_TrendTopics t ON t.Id = v.TrendTopicId
WHERE v.IsActive = 1;
GO

-- CF_V_WeeklyPublishChart: Last 7 days publish counts
CREATE OR ALTER VIEW CF_V_WeeklyPublishChart AS
SELECT
    CAST(PublishedAt AS DATE) AS PublishDate,
    COUNT(*) AS VideoCount
FROM CF_Videos
WHERE Status = 4
  AND PublishedAt >= DATEADD(DAY, -7, GETUTCDATE())
GROUP BY CAST(PublishedAt AS DATE);
GO

-- ── STORED PROCEDURES ──────────────────────────────────────────────────────

-- CF_SP_GetDueScheduledVideos: Videos ready to be published
CREATE OR ALTER PROCEDURE CF_SP_GetDueScheduledVideos
AS
BEGIN
    SET NOCOUNT ON;
    SELECT v.*, c.Name AS ChannelName
    FROM CF_Videos v
    INNER JOIN CF_Channels c ON c.Id = v.ChannelId
    WHERE v.Status = 6            -- Scheduled
      AND v.ScheduledAt <= GETUTCDATE()
      AND v.IsActive = 1
    ORDER BY v.ScheduledAt ASC;
END;
GO

-- CF_SP_UpdateChannelStats: Recalculates aggregates for a channel
CREATE OR ALTER PROCEDURE CF_SP_UpdateChannelStats
    @ChannelId INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE CF_Channels SET
        TotalVideosPublished = (
            SELECT COUNT(*) FROM CF_Videos
            WHERE ChannelId = @ChannelId AND Status = 4 AND IsActive = 1),
        TotalViews = (
            SELECT ISNULL(SUM(Views), 0) FROM CF_Videos
            WHERE ChannelId = @ChannelId AND IsActive = 1),
        UpdatedAt = GETUTCDATE()
    WHERE Id = @ChannelId;
END;
GO

-- CF_SP_CleanupExpiredTrends: Marks stale trends as inactive
CREATE OR ALTER PROCEDURE CF_SP_CleanupExpiredTrends
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE CF_TrendTopics
    SET IsActive  = 0,
        Status    = 3,           -- Declining
        UpdatedAt = GETUTCDATE()
    WHERE ExpiresAt < GETUTCDATE()
      AND IsActive = 1;

    SELECT @@ROWCOUNT AS DeactivatedCount;
END;
GO

PRINT 'HgermanAppsDB — CF_ schema created successfully.';
GO
