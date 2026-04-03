using HgermanContentFactory.Core.Interfaces;

namespace HgermanContentFactory.Worker.Jobs;

// ── Video Scheduler Job ────────────────────────────────────────────────────
// Runs every hour — checks schedules and enqueues videos due today

public class VideoSchedulerJob : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<VideoSchedulerJob> _logger;

    public VideoSchedulerJob(IServiceProvider services, ILogger<VideoSchedulerJob> logger)
    {
        _services = services;
        _logger   = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("VideoSchedulerJob started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope     = _services.CreateScope();
                var schedulerService = scope.ServiceProvider.GetRequiredService<ISchedulerService>();
                await schedulerService.EnqueueDueVideosAsync();
                _logger.LogInformation("Scheduler tick completed at {Time}", DateTimeOffset.UtcNow);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "VideoSchedulerJob error");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}

// ── Trend Refresh Job ──────────────────────────────────────────────────────
// Runs every 6 hours — refreshes trending topics for all active channel combos

public class TrendRefreshJob : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<TrendRefreshJob> _logger;

    public TrendRefreshJob(IServiceProvider services, ILogger<TrendRefreshJob> logger)
    {
        _services = services;
        _logger   = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Stagger start by 10 min so jobs don't collide at boot
        await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
        _logger.LogInformation("TrendRefreshJob started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope   = _services.CreateScope();
                var trendService  = scope.ServiceProvider.GetRequiredService<ITrendAnalysisService>();
                await trendService.RefreshAllTrendsAsync();
                _logger.LogInformation("Trend refresh completed at {Time}", DateTimeOffset.UtcNow);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TrendRefreshJob error");
            }

            await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
        }
    }
}

// ── Scheduled Publish Job ──────────────────────────────────────────────────
// Runs every 5 minutes — picks up videos whose ScheduledAt time has arrived

public class ScheduledPublishJob : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ScheduledPublishJob> _logger;

    public ScheduledPublishJob(IServiceProvider services, ILogger<ScheduledPublishJob> logger)
    {
        _services = services;
        _logger   = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ScheduledPublishJob started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope      = _services.CreateScope();
                var videoRepo        = scope.ServiceProvider.GetRequiredService<Core.Interfaces.IVideoRepository>();
                var schedulerService = scope.ServiceProvider.GetRequiredService<ISchedulerService>();

                var due = await videoRepo.GetDueScheduledAsync();
                foreach (var v in due)
                {
                    _logger.LogInformation("Publishing scheduled video {Id}: {Title}", v.Id, v.Title);
                    await schedulerService.ProcessScheduledPublishAsync(v.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ScheduledPublishJob error");
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}
