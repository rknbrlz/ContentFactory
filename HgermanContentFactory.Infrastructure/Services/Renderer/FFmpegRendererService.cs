using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;

namespace HgermanContentFactory.Infrastructure.Services.Renderer;

/// <summary>
/// Assembles a vertical short-form video using FFmpeg.
/// Hybrid: stock clips (primary) + AI images (fallback)
/// Output: 1080x1920 H.264/AAC 30fps
/// </summary>
public class FFmpegRendererService
{
    private readonly IConfiguration _config;
    private readonly ILogger<FFmpegRendererService> _logger;

    private string FFmpegPath => _config["FFmpeg:Path"] ?? "ffmpeg";
    private string FFprobePath => _config["FFmpeg:ProbePath"] ?? "ffprobe";

    private const int Width = 1080;
    private const int Height = 1920;
    private const int FPS = 30;
    private const string VCodec = "libx264";
    private const string ACodec = "aac";
    private const string Preset = "fast";
    private const string CRF = "20";

    public FFmpegRendererService(IConfiguration config, ILogger<FFmpegRendererService> logger)
    {
        _config = config;
        _logger = logger;
    }

    // ── Main entry ─────────────────────────────────────────────────────────

    public async Task<string?> RenderAsync(RenderRequest req)
    {
        try
        {
            Directory.CreateDirectory(req.WorkingDir);
            _logger.LogInformation("FFmpeg render start — Video {Id}: {Title}", req.VideoId, req.Title);

            if (!File.Exists(req.AudioPath))
            {
                _logger.LogError("Audio not found: {P}", req.AudioPath);
                return null;
            }

            var duration = await GetAudioDurationAsync(req.AudioPath);
            _logger.LogInformation("Audio duration: {D}s", duration.ToString("F1"));

            var srtPath = Path.Combine(req.WorkingDir, "subtitles.srt");
            GenerateSrt(req.Script, duration, srtPath);

            var rawVideoPath = Path.Combine(req.WorkingDir, "raw_video.mp4");
            bool ok;

            if (req.StockClipPaths.Any())
            {
                _logger.LogInformation("Pipeline: stock clips ({N} clips)", req.StockClipPaths.Count);
                ok = await BuildFromStockClipsAsync(req.StockClipPaths, duration, rawVideoPath);

                if (!ok && req.SceneImagePaths.Any())
                {
                    _logger.LogWarning("Stock clips failed — falling back to AI images");
                    ok = await BuildFromImagesAsync(req.SceneImagePaths, duration, rawVideoPath);
                }
            }
            else if (req.SceneImagePaths.Any())
            {
                _logger.LogInformation("Pipeline: AI images ({N} images)", req.SceneImagePaths.Count);
                ok = await BuildFromImagesAsync(req.SceneImagePaths, duration, rawVideoPath);
            }
            else
            {
                _logger.LogError("No video sources provided");
                return null;
            }

            if (!ok) return null;

            ok = await MergeAsync(rawVideoPath, req.AudioPath, srtPath,
                                  req.BackgroundMusicPath, req.NicheColor, req.OutputPath);

            if (!ok) return null;

            _logger.LogInformation("Render complete: {P}", req.OutputPath);
            return req.OutputPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Render exception for video {Id}", req.VideoId);
            return null;
        }
    }

    // ── A: Stock clips ─────────────────────────────────────────────────────

    private async Task<bool> BuildFromStockClipsAsync(
        List<string> clips, double totalDuration, string outputPath)
    {
        double secPerClip = totalDuration / clips.Count;
        var workDir = Path.GetDirectoryName(outputPath)!;
        var trimmed = new List<string>();

        for (int i = 0; i < clips.Count; i++)
        {
            var dest = Path.Combine(workDir, "trimmed_" + i.ToString("D2") + ".mp4");
            var wVal = (Width * 2).ToString();
            var hVal = (Height * 2).ToString();
            var wStr = Width.ToString();
            var hStr = Height.ToString();

            var scaleFilter =
                "scale=" + wVal + ":" + hVal + ":force_original_aspect_ratio=increase," +
                "crop=" + wStr + ":" + hStr + ":(iw-" + wStr + ")/2:(ih-" + hStr + ")/2," +
                "setsar=1";

            var ok = await RunFFmpegAsync(
                "-i \"" + clips[i] + "\" " +
                "-ss 0 -t " + secPerClip.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + " " +
                "-vf \"" + scaleFilter + "\" " +
                "-r " + FPS + " -c:v " + VCodec + " -preset " + Preset + " -crf " + CRF + " " +
                "-an -y \"" + dest + "\"",
                "TrimClip[" + i + "]");

            if (ok) trimmed.Add(dest);
            else _logger.LogWarning("Trim failed for clip {I}", i);
        }

        if (!trimmed.Any()) return false;
        return await ConcatVideosAsync(trimmed, outputPath);
    }

    // ── B: AI images with Ken Burns ────────────────────────────────────────

    private async Task<bool> BuildFromImagesAsync(
        List<string> images, double totalDuration, string outputPath)
    {
        double secPerImage = totalDuration / images.Count;
        int zoomFrames = (int)(secPerImage * FPS);
        var workDir = Path.GetDirectoryName(outputPath)!;
        var processed = new List<string>();

        for (int i = 0; i < images.Count; i++)
        {
            var dest = Path.Combine(workDir, "zoomed_" + i.ToString("D2") + ".mp4");

            // Alternate zoom in / zoom out
            var zoomExpr = (i % 2 == 0) ? "min(zoom+0.0007,1.12)" : "max(zoom-0.0007,1.0)";

            // Alternate pan direction
            string xExpr;
            switch (i % 4)
            {
                case 0: xExpr = "iw/2-(iw/zoom/2)"; break;
                case 1: xExpr = "0"; break;
                case 2: xExpr = "iw-(iw/zoom)"; break;
                default: xExpr = "iw/2-(iw/zoom/2)"; break;
            }

            var w2 = (Width * 2).ToString();
            var h2 = (Height * 2).ToString();
            var wStr = Width.ToString();
            var hStr = Height.ToString();
            var fpsStr = FPS.ToString();
            var frStr = zoomFrames.ToString();

            var filter =
                "scale=" + w2 + ":" + h2 + ":force_original_aspect_ratio=increase," +
                "crop=" + w2 + ":" + h2 + ":(iw-" + w2 + ")/2:(ih-" + h2 + ")/2," +
                "zoompan=z='" + zoomExpr + "':x='" + xExpr + "':y='ih/2-(ih/zoom/2)':" +
                "s=" + wStr + "x" + hStr + ":d=" + frStr + ":fps=" + fpsStr;

            var secStr = secPerImage.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);

            var ok = await RunFFmpegAsync(
                "-loop 1 -t " + secStr + " -i \"" + images[i] + "\" " +
                "-vf \"" + filter + "\" " +
                "-c:v " + VCodec + " -preset " + Preset + " -crf " + CRF + " " +
                "-r " + fpsStr + " -pix_fmt yuv420p -an " +
                "-y \"" + dest + "\"",
                "ZoomImage[" + i + "]");

            if (ok) processed.Add(dest);
        }

        if (!processed.Any()) return false;
        return await ConcatVideosAsync(processed, outputPath);
    }

    // ── C: Concat segments ─────────────────────────────────────────────────

    private async Task<bool> ConcatVideosAsync(List<string> parts, string outputPath)
    {
        var listFile = Path.Combine(Path.GetDirectoryName(outputPath)!, "concat.txt");
        var sb = new StringBuilder();
        foreach (var p in parts)
            sb.AppendLine("file '" + p.Replace("'", "\\'") + "'");
        await File.WriteAllTextAsync(listFile, sb.ToString());

        return await RunFFmpegAsync(
            "-f concat -safe 0 -i \"" + listFile + "\" -c copy -y \"" + outputPath + "\"",
            "Concat");
    }

    // ── D: Merge audio + subtitles + music ────────────────────────────────

    private async Task<bool> MergeAsync(
        string videoPath, string audioPath, string srtPath,
        string? musicPath, string nicheColor, string outputPath)
    {
        var hexColor = nicheColor.TrimStart('#');

        string audioInput, audioFilter, audioMap;

        if (!string.IsNullOrEmpty(musicPath) && File.Exists(musicPath))
        {
            audioInput =
                "-i \"" + audioPath + "\" " +
                "-i \"" + musicPath + "\"";
            audioFilter =
                "-filter_complex \"[1:a]volume=1.0[voice];" +
                "[2:a]volume=0.15[bg];" +
                "[voice][bg]amix=inputs=2:duration=first:dropout_transition=2[aout]\"";
            audioMap = "-map 0:v -map \"[aout]\"";
        }
        else
        {
            audioInput = "-i \"" + audioPath + "\"";
            audioFilter = string.Empty;
            audioMap = "-map 0:v -map 1:a";
        }

        // Build subtitle style without path in vf (Windows path causes FFmpeg parse errors)
        // Instead burn subtitles in a separate pass if needed, or use ASS format
        // For now: skip subtitles if SRT path contains Windows drive letter (colon issue)
        string vfFilter;
        bool hasDriveLetter = srtPath.Length >= 2 && srtPath[1] == ':';

        if (hasDriveLetter)
        {
            // Convert C:\path\file.srt → C:/path/file.srt and escape the colon
            var srtForFFmpeg = srtPath.Replace("\\", "/");
            // Escape colon in drive letter: C:/... → C\:/...
            srtForFFmpeg = srtForFFmpeg[0] + "\\:" + srtForFFmpeg.Substring(2);

            var style =
                "FontName=Arial,FontSize=21,Bold=1," +
                "PrimaryColour=&H00FFFFFF," +
                "OutlineColour=&H00" + hexColor + "," +
                "Outline=2,Shadow=1,Alignment=2,MarginV=90";

            vfFilter = "-vf \"subtitles='" + srtForFFmpeg + "':force_style='" + style + "'\"";
        }
        else
        {
            // Linux/Mac path — straightforward
            var style =
                "FontName=Arial,FontSize=21,Bold=1," +
                "PrimaryColour=&H00FFFFFF," +
                "OutlineColour=&H00" + hexColor + "," +
                "Outline=2,Shadow=1,Alignment=2,MarginV=90";
            vfFilter = "-vf \"subtitles='" + srtPath + "':force_style='" + style + "'\"";
        }

        return await RunFFmpegAsync(
            "-i \"" + videoPath + "\" " + audioInput + " " +
            vfFilter + " " +
            audioFilter + " " + audioMap + " " +
            "-c:v " + VCodec + " -preset " + Preset + " -crf " + CRF + " " +
            "-c:a " + ACodec + " -b:a 192k " +
            "-r " + FPS + " -pix_fmt yuv420p -movflags +faststart " +
            "-y \"" + outputPath + "\"",
            "MergeAudioSubs");
    }

    // ── Audio duration probe ───────────────────────────────────────────────

    public async Task<double> GetAudioDurationAsync(string audioPath)
    {
        try
        {
            var psi = new ProcessStartInfo(FFmpegPath, "-i \"" + audioPath + "\" -f null -")
            {
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi)!;
            var stderr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            var m = System.Text.RegularExpressions.Regex.Match(
                stderr, @"Duration:\s+(\d+):(\d+):([\d.]+)");
            if (m.Success)
                return int.Parse(m.Groups[1].Value) * 3600
                     + int.Parse(m.Groups[2].Value) * 60
                     + double.Parse(m.Groups[3].Value,
                           System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Duration probe failed"); }
        return 55.0;
    }

    // ── SRT subtitle builder ───────────────────────────────────────────────

    private static void GenerateSrt(string script, double totalDuration, string srtPath)
    {
        var words = script.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var chunk = new List<string>();

        foreach (var word in words)
        {
            chunk.Add(word);
            bool end = chunk.Count >= 5
                || word.EndsWith('.') || word.EndsWith('!')
                || word.EndsWith('?') || word.EndsWith(',');
            if (end) { lines.Add(string.Join(" ", chunk)); chunk.Clear(); }
        }
        if (chunk.Any()) lines.Add(string.Join(" ", chunk));

        double secPerLine = totalDuration / Math.Max(lines.Count, 1);
        var sb = new StringBuilder();

        for (int i = 0; i < lines.Count; i++)
        {
            var s = TimeSpan.FromSeconds(i * secPerLine);
            var e = TimeSpan.FromSeconds((i + 1) * secPerLine - 0.08);
            sb.AppendLine((i + 1).ToString());
            sb.AppendLine(FormatTs(s) + " --> " + FormatTs(e));
            sb.AppendLine(lines[i]);
            sb.AppendLine();
        }

        File.WriteAllText(srtPath, sb.ToString(), Encoding.UTF8);
    }

    private static string FormatTs(TimeSpan t) =>
        ((int)t.TotalHours).ToString("D2") + ":" +
        t.Minutes.ToString("D2") + ":" +
        t.Seconds.ToString("D2") + "," +
        t.Milliseconds.ToString("D3");

    // ── FFmpeg runner ──────────────────────────────────────────────────────

    private async Task<bool> RunFFmpegAsync(string args, string step)
    {
        _logger.LogDebug("[{Step}] ffmpeg {Args}", step, args);

        var psi = new ProcessStartInfo(FFmpegPath, args)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("FFmpeg process failed to start");

            _ = Task.Run(async () =>
            {
                while (!proc.StandardError.EndOfStream)
                {
                    var line = await proc.StandardError.ReadLineAsync();
                    if (line != null) _logger.LogTrace("[FFmpeg/{S}] {L}", step, line);
                }
            });

            await proc.WaitForExitAsync();

            if (proc.ExitCode == 0)
            {
                _logger.LogInformation("[{Step}] completed", step);
                return true;
            }

            _logger.LogError("[{Step}] exit code {Code}", step, proc.ExitCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Step}] Exception — is FFmpeg on PATH?", step);
            return false;
        }
    }
}

// ── Render Request ─────────────────────────────────────────────────────────

public record RenderRequest
{
    public int VideoId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Script { get; init; } = string.Empty;
    public string Language { get; init; } = "English";
    public string Niche { get; init; } = "Technology";
    public string NicheColor { get; init; } = "#4f6ef7";
    public string AudioPath { get; init; } = string.Empty;
    public List<string> StockClipPaths { get; init; } = new();
    public List<string> SceneImagePaths { get; init; } = new();
    public string WorkingDir { get; init; } = string.Empty;
    public string OutputPath { get; init; } = string.Empty;
    public string? BackgroundMusicPath { get; init; }
}