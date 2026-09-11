using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using GeoMineralTrace.Pipeline.Abstractions;
using Microsoft.Extensions.Logging;

namespace GeoMineralTrace.Pipeline.Keyframes;

/// <summary>
/// Extracts keyframes via ffmpeg when available; otherwise creates a documented
/// sampling plan artifact so the pipeline remains operable offline.
/// </summary>
public sealed class FfmpegKeyframeExtractor : IKeyframeExtractor
{
    private readonly ILogger<FfmpegKeyframeExtractor>? _logger;

    public FfmpegKeyframeExtractor(ILogger<FfmpegKeyframeExtractor>? logger = null)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<KeyframeInfo>> ExtractAsync(
        string mediaPath,
        string outputDirectory,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);

        // Still images are themselves the keyframe — skip ffmpeg.
        var ext = Path.GetExtension(mediaPath);
        if (ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".webp", StringComparison.OrdinalIgnoreCase))
        {
            progress?.Report("Still image — using source as keyframe.");
            var dest = Path.Combine(outputDirectory, "frame_0001" + ext);
            File.Copy(mediaPath, dest, overwrite: true);
            return
            [
                new KeyframeInfo(dest, TimeSpan.Zero, 0, 1.0, "still-image")
            ];
        }

        var ffmpeg = ResolveFfmpeg();
        if (ffmpeg is null)
        {
            progress?.Report("ffmpeg not found — writing sampling plan (no bitmaps).");
            return await WriteSamplingPlanAsync(mediaPath, outputDirectory, cancellationToken)
                .ConfigureAwait(false);
        }

        progress?.Report("Extracting keyframes via ffmpeg…");

        // Short clips (YouTube Shorts / vertical AV1) often stall on the scene+showinfo filter.
        var durationHint = await TryProbeDurationSecondsAsync(ffmpeg, mediaPath, cancellationToken)
            .ConfigureAwait(false);

        List<string> files;
        List<TimeSpan> timestamps;

        if (durationHint is > 0 and <= 60)
        {
            progress?.Report($"Short media ({durationHint:0.#}s) — sampling at 1 fps…");
            files = await RunFpsSampleAsync(ffmpeg, mediaPath, outputDirectory, maxFrames: 30, cancellationToken)
                .ConfigureAwait(false);
            timestamps = Enumerable.Range(0, files.Count).Select(i => TimeSpan.FromSeconds(i)).ToList();
        }
        else
        {
            var pattern = Path.Combine(outputDirectory, "kf_%04d.jpg");
            var args =
                $"-y -i \"{mediaPath}\" -vf \"select='gt(scene\\,0.3)+isnan(prev_selected_t)+gte(t-prev_selected_t\\,3)',showinfo\" -vsync vfr -q:v 2 \"{pattern}\"";

            var (exit, stderr) = await RunFfmpegAsync(ffmpeg, args, TimeSpan.FromMinutes(3), cancellationToken)
                .ConfigureAwait(false);
            timestamps = ParseShowinfoTimestamps(stderr);
            files = Directory.GetFiles(outputDirectory, "kf_*.jpg")
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (files.Count == 0 || exit != 0)
            {
                progress?.Report("Scene filter yielded 0 frames — falling back to 1 fps sample.");
                files = await RunFpsSampleAsync(ffmpeg, mediaPath, outputDirectory, maxFrames: 30, cancellationToken)
                    .ConfigureAwait(false);
                timestamps = Enumerable.Range(0, files.Count).Select(i => TimeSpan.FromSeconds(i)).ToList();
            }
        }

        var results = new List<KeyframeInfo>();
        for (var i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ts = i < timestamps.Count ? timestamps[i] : TimeSpan.FromSeconds(i * 3);
            var quality = EstimateJpegQualityProxy(files[i]);
            results.Add(new KeyframeInfo(files[i], ts, i, quality, "ffmpeg"));
        }

        _logger?.LogInformation("Extracted {Count} keyframes from {Media}", results.Count, mediaPath);
        return results;
    }

    private static async Task<List<string>> RunFpsSampleAsync(
        string ffmpeg,
        string mediaPath,
        string outputDirectory,
        int maxFrames,
        CancellationToken cancellationToken)
    {
        var fallback = Path.Combine(outputDirectory, "ff_%04d.jpg");
        var fbArgs = $"-y -i \"{mediaPath}\" -vf fps=1 -q:v 3 -frames:v {maxFrames} \"{fallback}\"";
        await RunFfmpegAsync(ffmpeg, fbArgs, TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false);
        return Directory.GetFiles(outputDirectory, "ff_*.jpg")
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<double?> TryProbeDurationSecondsAsync(
        string ffmpeg,
        string mediaPath,
        CancellationToken cancellationToken)
    {
        var args = $"-hide_banner -i \"{mediaPath}\"";
        var (_, stderr) = await RunFfmpegAsync(ffmpeg, args, TimeSpan.FromSeconds(20), cancellationToken)
            .ConfigureAwait(false);
        var m = Regex.Match(stderr, @"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)");
        if (!m.Success) return null;
        var h = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var min = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        var sec = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        return h * 3600 + min * 60 + sec;
    }

    private static async Task<(int ExitCode, string Stderr)> RunFfmpegAsync(
        string ffmpeg,
        string args,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var proc = Process.Start(new ProcessStartInfo
        {
            FileName = ffmpeg,
            Arguments = args,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Failed to start ffmpeg.");

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);
        try
        {
            var stderrTask = proc.StandardError.ReadToEndAsync(linked.Token);
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(linked.Token);
            await proc.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            _ = await stdoutTask.ConfigureAwait(false);
            return (proc.ExitCode, stderr);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!proc.HasExited)
                    proc.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignore
            }

            return (-1, "ffmpeg timed out");
        }
    }

    private static async Task<IReadOnlyList<KeyframeInfo>> WriteSamplingPlanAsync(
        string mediaPath,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        var planPath = Path.Combine(outputDirectory, "keyframe-plan.txt");
        var text =
            $"""
             ffmpeg was not found on PATH or in common install locations.
             Planned sampling for: {mediaPath}
             - Scene-change threshold 0.3
             - Minimum interval 3 seconds
             - Fallback: 1 fps up to 30 frames
             Install ffmpeg and re-run analysis for bitmap keyframes.
             """;
        await File.WriteAllTextAsync(planPath, text, cancellationToken).ConfigureAwait(false);
        return
        [
            new KeyframeInfo(planPath, TimeSpan.Zero, 0, 0, "plan-only-no-ffmpeg")
        ];
    }

    public static string? ResolveFfmpeg()
    {
        var fromPath = FindOnPath("ffmpeg.exe") ?? FindOnPath("ffmpeg");
        if (fromPath is not null) return fromPath;

        var wingetLinks = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WinGet", "Links", "ffmpeg.exe");
        if (File.Exists(wingetLinks))
            return wingetLinks;

        // yt-dlp.FFmpeg winget package layout
        try
        {
            var packages = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WinGet", "Packages");
            if (Directory.Exists(packages))
            {
                var hit = Directory.EnumerateFiles(packages, "ffmpeg.exe", SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (hit is not null)
                    return hit;
            }
        }
        catch
        {
            // ignore
        }

        var candidates = new[]
        {
            @"C:\ffmpeg\bin\ffmpeg.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "bin", "ffmpeg.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            try
            {
                var full = Path.Combine(dir.Trim('"'), fileName);
                if (File.Exists(full)) return full;
            }
            catch
            {
                // ignore bad PATH entries
            }
        }

        return null;
    }

    private static List<TimeSpan> ParseShowinfoTimestamps(string stderr)
    {
        var list = new List<TimeSpan>();
        foreach (Match m in Regex.Matches(stderr, @"pts_time:(?<t>[\d\.]+)"))
        {
            if (double.TryParse(m.Groups["t"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var sec))
                list.Add(TimeSpan.FromSeconds(sec));
        }

        return list;
    }

    /// <summary>
    /// Cheap quality proxy: file size / (heuristic). Higher is generally sharper/less compressed.
    /// </summary>
    private static double EstimateJpegQualityProxy(string path)
    {
        try
        {
            var len = new FileInfo(path).Length;
            return Math.Clamp(len / 50_000.0, 0.05, 1.0);
        }
        catch
        {
            return 0.5;
        }
    }
}
