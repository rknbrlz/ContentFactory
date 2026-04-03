using HgermanContentFactory.Core.Interfaces;
using HgermanContentFactory.Infrastructure.Data;
using HgermanContentFactory.Infrastructure.Repositories;
using HgermanContentFactory.Infrastructure.Services;
using HgermanContentFactory.Infrastructure.Services.AI;
using HgermanContentFactory.Infrastructure.Services.Renderer;
using HgermanContentFactory.Infrastructure.Services.YouTube;
using HgermanContentFactory.Worker.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

var host = Host.CreateDefaultBuilder(args)
    .UseSerilog((ctx, cfg) => cfg
        .WriteTo.Console()
        .WriteTo.File("logs/worker-.txt", rollingInterval: RollingInterval.Day))
    .ConfigureServices((ctx, services) =>
    {
        var connStr = ctx.Configuration.GetConnectionString("HgermanAppsDB")
            ?? throw new InvalidOperationException("HgermanAppsDB connection string missing.");

        services.AddDbContext<AppDbContext>(opt =>
            opt.UseSqlServer(connStr, sql => sql.EnableRetryOnFailure(3)));

        services.AddHttpClient<IContentGenerationService, ContentGenerationService>();
        services.AddHttpClient<ElevenLabsService>();
        services.AddHttpClient<ImageGenerationService>();
        services.AddHttpClient<StockVideoService>();

        services.AddScoped<FFmpegRendererService>();
        services.AddScoped<VideoRenderOrchestrator>();
        services.AddScoped<IYouTubeService, YouTubeService>();
        services.AddScoped<IVideoGenerationService, VideoGenerationService>();
        services.AddScoped<ITrendAnalysisService, TrendAnalysisService>();
        services.AddScoped<ISchedulerService, SchedulerService>();
        services.AddScoped<IChannelRepository, ChannelRepository>();
        services.AddScoped<IVideoRepository, VideoRepository>();
        services.AddScoped<ITrendRepository, TrendRepository>();

        services.AddHostedService<VideoSchedulerJob>();
        services.AddHostedService<TrendRefreshJob>();
        services.AddHostedService<ScheduledPublishJob>();
    })
    .Build();

await host.RunAsync();