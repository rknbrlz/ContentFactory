using Hangfire;
using Hangfire.SqlServer;
using HgermanContentFactory.Core.Interfaces;
using HgermanContentFactory.Infrastructure.Data;
using HgermanContentFactory.Infrastructure.Repositories;
using HgermanContentFactory.Infrastructure.Services;
using HgermanContentFactory.Infrastructure.Services.AI;
using HgermanContentFactory.Infrastructure.Services.Renderer;
using HgermanContentFactory.Infrastructure.Services.TurkishDrama;
using HgermanContentFactory.Infrastructure.Services.YouTube;
using HgermanContentFactory.Web.Hubs;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ── Serilog ────────────────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.Console()
    .WriteTo.File("logs/hgerman-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();
builder.Host.UseSerilog();

// ── Database ───────────────────────────────────────────────────────────────
var connStr = builder.Configuration.GetConnectionString("HgermanAppsDB")
    ?? throw new InvalidOperationException("Connection string 'HgermanAppsDB' not found.");

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlServer(connStr, sql =>
    {
        sql.EnableRetryOnFailure(3);
        sql.CommandTimeout(60);
    }));

// ── Hangfire ───────────────────────────────────────────────────────────────
builder.Services.AddHangfire(cfg => cfg
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(connStr, new SqlServerStorageOptions
    {
        CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
        SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
        QueuePollInterval = TimeSpan.Zero,
        UseRecommendedIsolationLevel = true,
        DisableGlobalLocks = true
    }));
builder.Services.AddHangfireServer(opt => opt.WorkerCount = 5);

// ── Repositories ───────────────────────────────────────────────────────────
builder.Services.AddScoped<IChannelRepository, ChannelRepository>();
builder.Services.AddScoped<IVideoRepository, VideoRepository>();
builder.Services.AddScoped<ITrendRepository, TrendRepository>();

// ── Application Services ───────────────────────────────────────────────────
builder.Services.AddHttpClient<IContentGenerationService, ContentGenerationService>();
builder.Services.AddHttpClient<ElevenLabsService>();
builder.Services.AddHttpClient<ImageGenerationService>();
builder.Services.AddHttpClient<StockVideoService>();
builder.Services.AddScoped<FFmpegRendererService>();
builder.Services.AddScoped<VideoRenderOrchestrator>();
builder.Services.AddScoped<IYouTubeService, YouTubeService>();
builder.Services.AddScoped<IVideoGenerationService, VideoGenerationService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<ITrendAnalysisService, TrendAnalysisService>();
builder.Services.AddScoped<ISchedulerService, SchedulerService>();
builder.Services.AddHttpClient<ITurkishDramaService, TurkishDramaService>();

// ── Turkish Drama Service ───────────────────────────────────────────────────
builder.Services.AddHttpClient<ITurkishDramaService, TurkishDramaService>();

// ── SignalR ────────────────────────────────────────────────────────────────
builder.Services.AddSignalR();

// ── MVC ────────────────────────────────────────────────────────────────────
builder.Services.AddControllersWithViews();

var app = builder.Build();

// ── Pipeline ───────────────────────────────────────────────────────────────
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    DashboardTitle = "Hgerman Content Factory — Scheduler"
});

app.MapHub<VideoProgressHub>("/hubs/progress");
app.MapControllerRoute("default", "{controller=Dashboard}/{action=Index}/{id?}");

// ── Recurring Jobs ─────────────────────────────────────────────────────────
RecurringJob.AddOrUpdate<ISchedulerService>(
    "enqueue-daily-videos",
    s => s.EnqueueDueVideosAsync(),
    "0 * * * *");

RecurringJob.AddOrUpdate<ITrendAnalysisService>(
    "refresh-trends",
    s => s.RefreshAllTrendsAsync(),
    "0 */6 * * *");

// ── Auto-migrate ───────────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();