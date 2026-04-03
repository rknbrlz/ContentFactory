using HgermanContentFactory.Core.Entities;
using HgermanContentFactory.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace HgermanContentFactory.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // ── Tables ──────────────────────────────────────────────────────────────
    public DbSet<CF_Channel>           CF_Channels           => Set<CF_Channel>();
    public DbSet<CF_Video>             CF_Videos             => Set<CF_Video>();
    public DbSet<CF_TrendTopic>        CF_TrendTopics        => Set<CF_TrendTopic>();
    public DbSet<CF_Campaign>          CF_Campaigns          => Set<CF_Campaign>();
    public DbSet<CF_Schedule>          CF_Schedules          => Set<CF_Schedule>();
    public DbSet<CF_ApiKey>            CF_ApiKeys            => Set<CF_ApiKey>();
    public DbSet<CF_PublishLog>        CF_PublishLogs        => Set<CF_PublishLog>();
    public DbSet<CF_AnalyticsSnapshot> CF_AnalyticsSnapshots => Set<CF_AnalyticsSnapshot>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // ── CF_Channels ──────────────────────────────────────────────────────
        b.Entity<CF_Channel>(e =>
        {
            e.ToTable("CF_Channels");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.YouTubeAccessToken).HasMaxLength(2000);
            e.Property(x => x.YouTubeRefreshToken).HasMaxLength(2000);
            e.Property(x => x.YouTubeChannelId).HasMaxLength(100);
            e.Property(x => x.ThumbnailStyle).HasMaxLength(200);
            e.Property(x => x.DefaultHashtags).HasMaxLength(500);
            e.HasMany(x => x.Videos)
             .WithOne(x => x.Channel)
             .HasForeignKey(x => x.ChannelId)
             .OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Campaigns)
             .WithOne(x => x.Channel)
             .HasForeignKey(x => x.ChannelId)
             .OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Schedules)
             .WithOne(x => x.Channel)
             .HasForeignKey(x => x.ChannelId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Analytics)
             .WithOne(x => x.Channel)
             .HasForeignKey(x => x.ChannelId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── CF_TrendTopics ───────────────────────────────────────────────────
        b.Entity<CF_TrendTopic>(e =>
        {
            e.ToTable("CF_TrendTopics");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.Keywords).HasMaxLength(500);
            e.Property(x => x.SourceUrls).HasMaxLength(2000);
            e.HasIndex(x => new { x.Language, x.Niche, x.TrendScore });
        });

        // ── CF_Campaigns ─────────────────────────────────────────────────────
        b.Entity<CF_Campaign>(e =>
        {
            e.ToTable("CF_Campaigns");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.ContentStyle).HasMaxLength(500);
            e.Property(x => x.TargetAudience).HasMaxLength(500);
        });

        // ── CF_Videos ────────────────────────────────────────────────────────
        b.Entity<CF_Video>(e =>
        {
            e.ToTable("CF_Videos");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(500).IsRequired();
            e.Property(x => x.Description).HasMaxLength(5000);
            e.Property(x => x.Script).HasMaxLength(20000);
            e.Property(x => x.Hashtags).HasMaxLength(1000);
            e.Property(x => x.YouTubeVideoId).HasMaxLength(50);
            e.Property(x => x.ThumbnailUrl).HasMaxLength(500);
            e.Property(x => x.VideoFilePath).HasMaxLength(500);
            e.Property(x => x.AudioFilePath).HasMaxLength(500);
            e.Property(x => x.ErrorMessage).HasMaxLength(2000);
            e.Property(x => x.AIPromptUsed).HasMaxLength(2000);
            e.HasOne(x => x.TrendTopic)
             .WithMany(x => x.Videos)
             .HasForeignKey(x => x.TrendTopicId)
             .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Campaign)
             .WithMany(x => x.Videos)
             .HasForeignKey(x => x.CampaignId)
             .OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.ScheduledAt);
            e.HasIndex(x => new { x.ChannelId, x.Status });
        });

        // ── CF_Schedules ─────────────────────────────────────────────────────
        b.Entity<CF_Schedule>(e =>
        {
            e.ToTable("CF_Schedules");
            e.HasKey(x => x.Id);
            e.Property(x => x.PublishTimes).HasMaxLength(200);
        });

        // ── CF_ApiKeys ───────────────────────────────────────────────────────
        b.Entity<CF_ApiKey>(e =>
        {
            e.ToTable("CF_ApiKeys");
            e.HasKey(x => x.Id);
            e.Property(x => x.Provider).HasMaxLength(100).IsRequired();
            e.Property(x => x.KeyName).HasMaxLength(100).IsRequired();
            e.Property(x => x.EncryptedKey).HasMaxLength(2000).IsRequired();
        });

        // ── CF_PublishLogs ───────────────────────────────────────────────────
        b.Entity<CF_PublishLog>(e =>
        {
            e.ToTable("CF_PublishLogs");
            e.HasKey(x => x.Id);
            e.Property(x => x.PlatformVideoId).HasMaxLength(100);
            e.Property(x => x.PlatformUrl).HasMaxLength(500);
            e.Property(x => x.ErrorMessage).HasMaxLength(2000);
            e.HasOne(x => x.Video)
             .WithMany()
             .HasForeignKey(x => x.VideoId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── CF_AnalyticsSnapshots ────────────────────────────────────────────
        b.Entity<CF_AnalyticsSnapshot>(e =>
        {
            e.ToTable("CF_AnalyticsSnapshots");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ChannelId, x.SnapshotDate });
        });
    }
}
