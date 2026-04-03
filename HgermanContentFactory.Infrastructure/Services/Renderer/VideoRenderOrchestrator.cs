using HgermanContentFactory.Core.Enums;
using HgermanContentFactory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HgermanContentFactory.Infrastructure.Services.Renderer;

/// <summary>
/// Orchestrates the full hybrid video production pipeline:
///
///   [1] ElevenLabs TTS      → narration.mp3
///   [2] Pexels Stock Clips  → clip_00.mp4 … clip_04.mp4  (PRIMARY)
///   [3] DALL-E 3 Images     → scene_00.png … (FALLBACK if Pexels fails)
///   [4] DALL-E 3 Thumbnail  → thumbnail.png
///   [5] FFmpeg Assembly     → final_{id}.mp4 (1080×1920, 30fps)
/// </summary>
public class VideoRenderOrchestrator
{
    private readonly AppDbContext _db;
    private readonly ElevenLabsService _tts;
    private readonly StockVideoService _stock;
    private readonly ImageGenerationService _images;
    private readonly FFmpegRendererService _ffmpeg;
    private readonly IConfiguration _config;
    private readonly ILogger<VideoRenderOrchestrator> _logger;

    private static readonly Dictionary<string, string> NicheColors = new()
    {
        ["Technology"]    = "#4f6ef7",
        ["Finance"]       = "#f59e0b",
        ["Health"]        = "#22c55e",
        ["Gaming"]        = "#a855f7",
        ["Education"]     = "#06b6d4",
        ["Lifestyle"]     = "#f97316",
        ["Food"]          = "#ef4444",
        ["Travel"]        = "#14b8a6",
        ["Science"]       = "#6366f1",
        ["Entertainment"] = "#ec4899",
        ["Business"]      = "#0ea5e9",
        ["Sports"]        = "#f43f5e",
        ["Nature"]        = "#84cc16",
        ["Fashion"]       = "#d946ef",
        ["DIY"]           = "#fb923c",
    };

    public VideoRenderOrchestrator(
        AppDbContext db,
        ElevenLabsService tts,
        StockVideoService stock,
        ImageGenerationService images,
        FFmpegRendererService ffmpeg,
        IConfiguration config,
        ILogger<VideoRenderOrchestrator> logger)
    {
        _db     = db;
        _tts    = tts;
        _stock  = stock;
        _images = images;
        _ffmpeg = ffmpeg;
        _config = config;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Main pipeline
    // ─────────────────────────────────────────────────────────────────────

    public async Task<bool> RenderVideoAsync(int videoId)
    {
        var video = await _db.CF_Videos
            .Include(v => v.Channel)
            .FirstOrDefaultAsync(v => v.Id == videoId);

        if (video == null)
        {
            _logger.LogError("Video {Id} not found", videoId);
            return false;
        }

        if (string.IsNullOrWhiteSpace(video.Script))
        {
            await FailAsync(video, "Script is empty — cannot render");
            return false;
        }

        var storageRoot = _config["Storage:VideoPath"]
            ?? Path.Combine(Directory.GetCurrentDirectory(), "VideoStorage");
        var workDir     = Path.Combine(storageRoot, $"video_{videoId}");
        var outputPath  = Path.Combine(workDir, $"final_{videoId}.mp4");
        Directory.CreateDirectory(workDir);

        var language   = video.Language.ToString();
        var niche      = video.Niche.ToString();
        var nicheColor = NicheColors.GetValueOrDefault(niche, "#4f6ef7");

        _logger.LogInformation(
            "=== RENDER START  Video:{Id}  Lang:{L}  Niche:{N} ===",
            videoId, language, niche);

        try
        {
            // ── STEP 1: Text-to-Speech ─────────────────────────────────────
            _logger.LogInformation("[1/5] ElevenLabs TTS...");
            var audioPath = Path.Combine(workDir, "narration.mp3");
            if (!await _tts.TextToSpeechAsync(video.Script, language, audioPath))
            {
                await FailAsync(video, "ElevenLabs TTS failed");
                return false;
            }

            // ── STEP 2: Stock Video Clips (Pexels — PRIMARY) ───────────────
            _logger.LogInformation("[2/5] Downloading Pexels stock clips...");
            var clipsDir   = Path.Combine(workDir, "clips");
            var stockClips = await _stock.DownloadClipsAsync(
                niche, video.Title, clipsDir, count: 5, maxDurationSec: 15);

            if (stockClips.Any())
                _logger.LogInformation("  ✓ {N} stock clips downloaded", stockClips.Count);
            else
                _logger.LogWarning("  ✗ No stock clips — will use AI images as fallback");

            // ── STEP 3: AI Scene Images (DALL-E 3 — FALLBACK) ─────────────
            List<string> scenePaths = [];

            if (!stockClips.Any())
            {
                _logger.LogInformation("[3/5] Generating DALL-E 3 scene images (fallback)...");
                var imagesDir = Path.Combine(workDir, "scenes");
                scenePaths = await _images.GenerateSceneImagesAsync(
                    video.Title, video.Script, niche, imagesDir, count: 5);

                if (!scenePaths.Any())
                {
                    await FailAsync(video, "Both stock clips and AI image generation failed");
                    return false;
                }
                _logger.LogInformation("  ✓ {N} AI images generated", scenePaths.Count);
            }
            else
            {
                _logger.LogInformation("[3/5] Skipping AI images (stock clips available)");
            }

            // ── STEP 4: Thumbnail ──────────────────────────────────────────
            _logger.LogInformation("[4/5] Generating thumbnail...");
            var thumbPath   = Path.Combine(workDir, "thumbnail.png");
            var thumbResult = await _images.GenerateThumbnailAsync(
                video.Title, niche, thumbPath);

            // ── STEP 5: FFmpeg Assembly ────────────────────────────────────
            _logger.LogInformation("[5/5] FFmpeg assembly...");
            var finalPath = await _ffmpeg.RenderAsync(new RenderRequest
            {
                VideoId             = videoId,
                Title               = video.Title,
                Script              = video.Script,
                Language            = language,
                Niche               = niche,
                NicheColor          = nicheColor,
                AudioPath           = audioPath,
                StockClipPaths      = stockClips,    // primary
                SceneImagePaths     = scenePaths,    // fallback
                WorkingDir          = workDir,
                OutputPath          = outputPath,
                BackgroundMusicPath = GetBackgroundMusic(niche)
            });

            if (string.IsNullOrEmpty(finalPath))
            {
                await FailAsync(video, "FFmpeg assembly failed");
                return false;
            }

            // ── Update DB ──────────────────────────────────────────────────
            video.VideoFilePath   = finalPath;
            video.AudioFilePath   = audioPath;
            video.ThumbnailUrl    = thumbResult != null
                ? $"/VideoStorage/video_{videoId}/thumbnail.png"
                : null;
            video.DurationSeconds = await GetVideoLengthAsync(finalPath);
            video.Status          = VideoStatus.Rendered;
            video.UpdatedAt       = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "=== RENDER COMPLETE  {Path}  ({Dur}s) ===",
                finalPath, video.DurationSeconds);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Render pipeline exception for video {Id}", videoId);
            await FailAsync(video, ex.Message);
            return false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private async Task FailAsync(Core.Entities.CF_Video video, string reason)
    {
        video.Status       = VideoStatus.Failed;
        video.ErrorMessage = reason;
        video.UpdatedAt    = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        _logger.LogError("Video {Id} FAILED: {Reason}", video.Id, reason);
    }

    private string? GetBackgroundMusic(string niche)
    {
        var musicDir = _config["Storage:MusicPath"];
        if (string.IsNullOrEmpty(musicDir) || !Directory.Exists(musicDir)) return null;

        return new[]
        {
            Path.Combine(musicDir, $"{niche.ToLower()}.mp3"),
            Path.Combine(musicDir, "default.mp3"),
            Path.Combine(musicDir, "background.mp3")
        }.FirstOrDefault(File.Exists);
    }

    private async Task<int> GetVideoLengthAsync(string path)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(
                "FFprobePath",
                $"-v error -show_entries format=duration " +
                $"-of default=noprint_wrappers=1:nokey=1 \"{path}\"")
            {
                RedirectStandardOutput = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };
            using var proc = System.Diagnostics.Process.Start(psi)!;
            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            if (double.TryParse(output.Trim(),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var secs))
                return (int)secs;
        }
        catch { /* ignore */ }
        return 55;
    }
}
