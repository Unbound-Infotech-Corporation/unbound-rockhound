using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using GeoMineralTrace.Core.App;
using Microsoft.Extensions.Logging;

namespace GeoMineralTrace.Pipeline.Ingest;

public sealed record YouTubeDownloadResult(
    string LocalFilePath,
    string SourceUrl,
    string? Title,
    string ToolUsed,
    bool IsStreamWorkingCopy = false,
    IReadOnlyList<string>? AllLocalPaths = null);

public sealed record RemoteMediaRef(
    RemoteMediaPlatform Platform,
    string MediaId,
    string CanonicalUrl,
    string EmbedHtml);

/// <summary>Backward-compatible alias.</summary>
public sealed record YouTubeVideoRef(
    string VideoId,
    string CanonicalWatchUrl,
    string EmbedHtml);

/// <summary>
/// YouTube + Instagram ingest via yt-dlp: stream working copies and archival downloads.
/// In-app preview uses embed HTML from <see cref="TryCreateMediaRef"/>.
/// </summary>
public sealed class YouTubeMediaFetcher
{
    private static readonly Regex YouTubeUrlPattern = new(
        @"^(https?://)?(www\.)?(youtube\.com/(watch\?v=|shorts/|live/|embed/)|youtu\.be/)[\w\-?=&#.%]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex InstagramUrlPattern = new(
        @"^(https?://)?(www\.)?instagram\.com/(p|reel|reels|tv)/[\w\-]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex YouTubeVideoIdPattern = new(
        @"(?:youtube\.com/(?:watch\?(?:[^#]*&)?v=|shorts/|embed/|live/)|youtu\.be/)([A-Za-z0-9_-]{6,})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex InstagramShortcodePattern = new(
        @"instagram\.com/(?:p|reel|reels|tv)/([A-Za-z0-9_-]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ILogger<YouTubeMediaFetcher>? _logger;

    public YouTubeMediaFetcher(ILogger<YouTubeMediaFetcher>? logger = null)
    {
        _logger = logger;
    }

    public static bool IsSupportedUrl(string? url) => GetPlatform(url) is not null;

    public static RemoteMediaPlatform? GetPlatform(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        url = url.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;
        if (uri.Scheme is not ("http" or "https"))
            return null;

        if (YouTubeUrlPattern.IsMatch(url)
            || uri.Host.Contains("youtube", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
            return RemoteMediaPlatform.YouTube;

        if (InstagramUrlPattern.IsMatch(url)
            || uri.Host.Contains("instagram.com", StringComparison.OrdinalIgnoreCase))
            return RemoteMediaPlatform.Instagram;

        return null;
    }

    public static string? TryGetMediaId(string? url)
    {
        var platform = GetPlatform(url);
        if (platform is null || string.IsNullOrWhiteSpace(url))
            return null;

        url = url.Trim();
        if (platform == RemoteMediaPlatform.YouTube)
        {
            var m = YouTubeVideoIdPattern.Match(url);
            if (m.Success) return m.Groups[1].Value;

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return null;

            foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split('=', 2);
                if (kv.Length == 2 && kv[0].Equals("v", StringComparison.OrdinalIgnoreCase) && kv[1].Length > 0)
                    return Uri.UnescapeDataString(kv[1]);
            }

            return null;
        }

        var ig = InstagramShortcodePattern.Match(url);
        return ig.Success ? ig.Groups[1].Value : null;
    }

    public static string? TryGetVideoId(string? url) =>
        GetPlatform(url) == RemoteMediaPlatform.YouTube ? TryGetMediaId(url) : null;

    /// <summary>Canonical watch URL without playlist/index params (avoids yt-dlp playlist confusion).</summary>
    public static string? NormalizeYouTubeWatchUrl(string? url)
    {
        var id = TryGetVideoId(url);
        return id is null ? null : $"https://www.youtube.com/watch?v={id}";
    }

    public static RemoteMediaRef? TryCreateMediaRef(string url)
    {
        var platform = GetPlatform(url);
        var id = TryGetMediaId(url);
        if (platform is null || id is null)
            return null;

        if (platform == RemoteMediaPlatform.YouTube)
        {
            var watch = $"https://www.youtube.com/watch?v={id}";
            var html = YouTubeIframePlayerHtml.Build(id);
            return new RemoteMediaRef(platform.Value, id, watch, html);
        }

        var kind = url.Contains("/reel/", StringComparison.OrdinalIgnoreCase)
                   || url.Contains("/reels/", StringComparison.OrdinalIgnoreCase)
            ? "reel"
            : url.Contains("/tv/", StringComparison.OrdinalIgnoreCase)
                ? "tv"
                : "p";
        var canonical = kind switch
        {
            "reel" => $"https://www.instagram.com/reel/{id}/",
            "tv" => $"https://www.instagram.com/tv/{id}/",
            _ => $"https://www.instagram.com/p/{id}/"
        };
        var embedPath = kind == "p" ? $"p/{id}" : $"reel/{id}";
        var igHtml =
            "<!DOCTYPE html><html><head><meta charset=\"utf-8\"/>" +
            "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"/>" +
            "<style>html,body{margin:0;height:100%;background:#0f0f12;overflow:hidden;display:flex;align-items:center;justify-content:center}" +
            "iframe{border:0;max-width:100%;max-height:100%}</style></head><body>" +
            $"<iframe src=\"https://www.instagram.com/{embedPath}/embed\" width=\"400\" height=\"480\" " +
            "allowfullscreen scrolling=\"no\"></iframe></body></html>";
        return new RemoteMediaRef(platform.Value, id, canonical, igHtml);
    }

    public static YouTubeVideoRef? TryCreateVideoRef(string url)
    {
        var media = TryCreateMediaRef(url);
        if (media is null || media.Platform != RemoteMediaPlatform.YouTube)
            return null;
        return new YouTubeVideoRef(media.MediaId, media.CanonicalUrl, media.EmbedHtml);
    }

    public static string? ResolveYtDlp() =>
        ResolveTool(
            ["yt-dlp.exe", "yt-dlp", "youtube-dl.exe", "youtube-dl"],
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "WinGet", "Links", "yt-dlp.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs", "yt-dlp", "yt-dlp.exe"),
                @"C:\yt-dlp\yt-dlp.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "scoop", "apps", "yt-dlp", "current", "yt-dlp.exe")
            ]);

    public static string? ResolveFfmpeg() =>
        ResolveTool(
            ["ffmpeg.exe", "ffmpeg"],
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "WinGet", "Links", "ffmpeg.exe"),
                ..EnumerateWinGetPackageBins("yt-dlp.FFmpeg*", "ffmpeg.exe"),
                ..EnumerateWinGetPackageBins("Gyan.FFmpeg*", "ffmpeg.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "scoop", "apps", "ffmpeg", "current", "ffmpeg.exe")
            ]);

    public static string? ResolveDeno() =>
        ResolveTool(
            ["deno.exe", "deno"],
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "WinGet", "Links", "deno.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".deno", "bin", "deno.exe")
            ]);

    public static bool IsYouTubeLiveUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;
        url = url.Trim();
        return url.Contains("/live/", StringComparison.OrdinalIgnoreCase)
               || url.Contains("youtube.com/live/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Downloads only the selected time range via yt-dlp --download-sections, then reuses the local pipeline.
    /// </summary>
    public async Task<YouTubeDownloadResult> PrepareClipAsync(
        string url,
        MediaClipRange clip,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (GetPlatform(url) != RemoteMediaPlatform.YouTube)
            throw new ArgumentException("Clip download is supported for YouTube URLs only.", nameof(url));

        if (IsYouTubeLiveUrl(url))
            throw new InvalidOperationException("Live streams cannot be clipped — wait until the broadcast ends and use the VOD URL.");

        var normalized = clip.Normalized();
        normalized.Validate();

        var id = TryGetMediaId(url)
                 ?? throw new ArgumentException("Could not parse a video id from the URL.", nameof(url));

        var cacheDir = AppDataPaths.Sub("clip-cache");
        Directory.CreateDirectory(cacheDir);

        var tag = $"{(int)Math.Round(normalized.StartSeconds * 1000)}-{(int)Math.Round(normalized.EndSeconds * 1000)}";
        var existing = Directory.GetFiles(cacheDir)
            .Where(f => Path.GetFileName(f).StartsWith($"{id}_{tag}.", StringComparison.OrdinalIgnoreCase)
                        && IsMediaExtension(Path.GetExtension(f)))
            .OrderByDescending(f => new FileInfo(f).Length)
            .FirstOrDefault();

        if (existing is not null && new FileInfo(existing).Length > 10_000)
        {
            progress?.Report($"Reusing cached clip: {Path.GetFileName(existing)}");
            return new YouTubeDownloadResult(existing, url, id, ResolveYtDlp() ?? "cache", IsStreamWorkingCopy: true);
        }

        progress?.Report($"Downloading clip {normalized.ToDownloadSectionSpec()}…");
        var outputTemplate = Path.Combine(cacheDir, $"%(id)s_{tag}.%(ext)s");
        return await DownloadInternalAsync(
            url,
            cacheDir,
            RemoteMediaPlatform.YouTube,
            formatWhenFfmpeg: "b[height<=720][ext=mp4]/b[height<=720]/bv*[height<=720]+ba/b",
            formatWhenNoFfmpeg: "b[height<=720][ext=mp4]/b[height<=720]/best[height<=720]/best",
            progress,
            cancellationToken,
            isStreamWorkingCopy: true,
            downloadSection: normalized.ToDownloadSectionSpec(),
            outputTemplateOverride: outputTemplate).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds or reuses a capped (≤720p) working copy for OCR/keyframes/ASR.
    /// Preview should use the embed player — this is not a full archival download.
    /// </summary>
    public async Task<YouTubeDownloadResult> PrepareStreamWorkingCopyAsync(
        string url,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var id = TryGetMediaId(url)
                 ?? throw new ArgumentException("Could not parse a media id from the URL.", nameof(url));
        var platform = GetPlatform(url) ?? RemoteMediaPlatform.YouTube;

        var cacheDir = AppDataPaths.Sub("stream-cache");
        Directory.CreateDirectory(cacheDir);

        var existing = Directory.GetFiles(cacheDir)
            .Where(f => Path.GetFileName(f).StartsWith(id + ".", StringComparison.OrdinalIgnoreCase)
                        && IsMediaExtension(Path.GetExtension(f))
                        && !Regex.IsMatch(Path.GetFileName(f), @"\.f\d+\."))
            .OrderByDescending(f => new FileInfo(f).Length)
            .FirstOrDefault();

        if (existing is not null && new FileInfo(existing).Length > 10_000)
        {
            progress?.Report($"Reusing stream working copy: {Path.GetFileName(existing)}");
            return new YouTubeDownloadResult(existing, url, id, ResolveYtDlp() ?? "cache", IsStreamWorkingCopy: true);
        }

        progress?.Report("Preparing ≤720p working copy for analysis (preview streams in-app)…");
        var streamFormat = platform == RemoteMediaPlatform.Instagram
            ? "b/best"
            : "b[height<=720][ext=mp4]/b[height<=720]/bv*[height<=720]+ba/b";
        var streamFormatNoFfmpeg = platform == RemoteMediaPlatform.Instagram
            ? "b/best"
            : "b[height<=720][ext=mp4]/b[height<=720]/best[height<=720]/best";
        return await DownloadInternalAsync(
            url,
            cacheDir,
            platform,
            formatWhenFfmpeg: streamFormat,
            formatWhenNoFfmpeg: streamFormatNoFfmpeg,
            progress,
            cancellationToken,
            isStreamWorkingCopy: true).ConfigureAwait(false);
    }

    public Task<YouTubeDownloadResult> DownloadAsync(
        string url,
        string? outputDirectory = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        outputDirectory ??= AppDataPaths.Sub(
            "downloads",
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));

        var platform = GetPlatform(url) ?? RemoteMediaPlatform.YouTube;
        var formatFfmpeg = platform == RemoteMediaPlatform.Instagram
            ? "b/best"
            : "b[ext=mp4]/b/bv*[ext=mp4]+ba[ext=m4a]/best";
        var formatNoFfmpeg = platform == RemoteMediaPlatform.Instagram
            ? "b/best"
            : "b[ext=mp4]/b/best[ext=mp4]/best";

        return DownloadInternalAsync(
            url,
            outputDirectory,
            platform,
            formatWhenFfmpeg: formatFfmpeg,
            formatWhenNoFfmpeg: formatNoFfmpeg,
            progress,
            cancellationToken,
            isStreamWorkingCopy: false);
    }

    private async Task<YouTubeDownloadResult> DownloadInternalAsync(
        string url,
        string outputDirectory,
        RemoteMediaPlatform platform,
        string formatWhenFfmpeg,
        string formatWhenNoFfmpeg,
        IProgress<string>? progress,
        CancellationToken cancellationToken,
        bool isStreamWorkingCopy,
        string? downloadSection = null,
        string? outputTemplateOverride = null)
    {
        url = url.Trim();
        if (!IsSupportedUrl(url))
            throw new ArgumentException("Not a recognized YouTube or Instagram URL.", nameof(url));

        if (GetPlatform(url) == RemoteMediaPlatform.YouTube)
            url = NormalizeYouTubeWatchUrl(url) ?? url;

        var tool = ResolveYtDlp()
                   ?? throw new InvalidOperationException(
                       "yt-dlp was not found. Install it (winget install yt-dlp) and restart GeoMineral Trace, then try again.");

        var ffmpeg = ResolveFfmpeg();
        var deno = ResolveDeno();
        Directory.CreateDirectory(outputDirectory);

        progress?.Report(isStreamWorkingCopy
            ? $"Preparing {PlatformLabel(platform)} media via yt-dlp…"
            : $"Starting {PlatformLabel(platform)} download via yt-dlp…");
        _logger?.LogInformation(
            "{Platform} {Mode} {Url} with {Tool} (ffmpeg={Ffmpeg}, deno={Deno}) → {Dir}",
            platform,
            isStreamWorkingCopy ? "stream-copy" : "download",
            url, tool, ffmpeg ?? "(none)", deno ?? "(none)", outputDirectory);

        var mediaId = TryGetMediaId(url) ?? "media";
        var outputTemplate = outputTemplateOverride ?? (platform == RemoteMediaPlatform.Instagram
            ? Path.Combine(outputDirectory, "%(id)s_%(playlist_index)02d.%(ext)s")
            : Path.Combine(outputDirectory, "%(id)s.%(ext)s"));
        var format = ffmpeg is not null ? formatWhenFfmpeg : formatWhenNoFfmpeg;
        if (ffmpeg is null)
            progress?.Report("ffmpeg not found — using a single stream (install ffmpeg for best quality).");

        var playerClients = YouTubeSessionStore.HasSavedSession ? "web,mweb" : "mweb,web";
        var (exitCode, stdout, stderr) = await RunYtDlpAsync(
            tool,
            ffmpeg,
            deno,
            url,
            outputTemplate,
            format,
            platform,
            useImpersonate: true,
            playerClients,
            progress,
            cancellationToken,
            downloadSection).ConfigureAwait(false);

        if (exitCode != 0 && platform == RemoteMediaPlatform.YouTube && LooksLikeHttp403(stdout, stderr))
        {
            progress?.Report("YouTube returned 403 — retrying with alternate client…");
            (exitCode, stdout, stderr) = await RunYtDlpAsync(
                tool,
                ffmpeg,
                deno,
                url,
                outputTemplate,
                format: isStreamWorkingCopy ? "b[height<=720]/b/best" : "b/best",
                platform,
                useImpersonate: true,
                playerClients: "mweb",
                progress,
                cancellationToken,
                downloadSection).ConfigureAwait(false);
        }

        if (exitCode != 0)
        {
            var detail = Tail(stderr.Length > 0 ? stderr : stdout, 1200);
            throw new InvalidOperationException(FormatYtDlpFailure(platform, exitCode, detail, downloadSection is not null));
        }

        var allMedia = Directory.GetFiles(outputDirectory)
            .Where(f => IsMediaExtension(Path.GetExtension(f)))
            .OrderBy(f => Regex.IsMatch(Path.GetFileName(f), @"\.f\d+\.") ? 1 : 0)
            .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (allMedia.Count == 0)
        {
            // Fallback: match files prefixed with shortcode/id
            allMedia = Directory.GetFiles(outputDirectory)
                .Where(f => Path.GetFileName(f).StartsWith(mediaId, StringComparison.OrdinalIgnoreCase)
                            && IsMediaExtension(Path.GetExtension(f)))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var media = allMedia.OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();

        if (media is null)
            throw new InvalidOperationException("Prepare finished but no media file was found in the output folder.");

        progress?.Report($"{(isStreamWorkingCopy ? "Working copy ready" : "Downloaded")}: {Path.GetFileName(media)}");

        var (title, description, location, tags) = TryReadInfoJson(media, mediaId, outputDirectory);
        title ??= Path.GetFileNameWithoutExtension(media);

        try
        {
            await MediaSourceSidecar.WriteAsync(media, new MediaSourceSidecar
            {
                SourceUrl = url,
                Title = title,
                Description = description,
                Platform = platform.ToString(),
                MediaId = mediaId,
                LocationLabel = location,
                Tags = tags
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to write .gmt-source.json sidecar for {Media}", media);
        }

        return new YouTubeDownloadResult(
            media, url, title, tool, isStreamWorkingCopy, allMedia);
    }

    private static (string? Title, string? Description, string? Location, List<string> Tags) TryReadInfoJson(
        string mediaPath,
        string mediaId,
        string outputDirectory)
    {
        try
        {
            var candidates = new List<string>
            {
                Path.ChangeExtension(mediaPath, ".info.json"),
                Path.Combine(outputDirectory, mediaId + ".info.json")
            };
            candidates.AddRange(Directory.GetFiles(outputDirectory, "*.info.json"));

            foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!File.Exists(path)) continue;
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
                var root = doc.RootElement;
                string? title = root.TryGetProperty("title", out var t) ? t.GetString() : null;
                string? description = root.TryGetProperty("description", out var d) ? d.GetString() : null;
                string? location = null;
                if (root.TryGetProperty("location", out var locEl))
                    location = locEl.ValueKind == System.Text.Json.JsonValueKind.String
                        ? locEl.GetString()
                        : locEl.ToString();

                var tags = new List<string>();
                if (root.TryGetProperty("tags", out var tagsEl) &&
                    tagsEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var tag in tagsEl.EnumerateArray())
                    {
                        var s = tag.GetString();
                        if (!string.IsNullOrWhiteSpace(s))
                            tags.Add(s!);
                    }
                }

                if (!string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(description) ||
                    !string.IsNullOrWhiteSpace(location) || tags.Count > 0)
                    return (title, description, location, tags);
            }
        }
        catch
        {
            // Non-fatal — pipeline can still NER filename.
        }

        return (null, null, null, []);
    }

    private static string PlatformLabel(RemoteMediaPlatform platform) =>
        platform == RemoteMediaPlatform.Instagram ? "Instagram" : "YouTube";

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunYtDlpAsync(
        string tool,
        string? ffmpeg,
        string? deno,
        string url,
        string outputTemplate,
        string format,
        RemoteMediaPlatform platform,
        bool useImpersonate,
        string playerClients,
        IProgress<string>? progress,
        CancellationToken cancellationToken,
        string? downloadSection = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = tool,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        AugmentPath(psi, tool, ffmpeg, deno);

        if (ffmpeg is not null)
        {
            psi.ArgumentList.Add("--ffmpeg-location");
            psi.ArgumentList.Add(Path.GetDirectoryName(ffmpeg)!);
            psi.ArgumentList.Add("--merge-output-format");
            psi.ArgumentList.Add("mp4");
        }

        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add(format);

        if (deno is not null)
        {
            psi.ArgumentList.Add("--js-runtimes");
            psi.ArgumentList.Add($"deno:{deno}");
        }

        if (useImpersonate)
            psi.ArgumentList.Add("--impersonate=chrome");

        if (!string.IsNullOrWhiteSpace(downloadSection))
        {
            psi.ArgumentList.Add("--download-sections");
            psi.ArgumentList.Add(downloadSection);
            psi.ArgumentList.Add("--force-keyframes-at-cuts");
        }

        if (platform == RemoteMediaPlatform.YouTube)
        {
            if (YouTubeSessionStore.HasSavedSession)
            {
                psi.ArgumentList.Add("--cookies");
                psi.ArgumentList.Add(YouTubeSessionStore.CookieFilePath);
                progress?.Report("Using signed-in YouTube session…");
            }

            psi.ArgumentList.Add("--extractor-args");
            psi.ArgumentList.Add($"youtube:player_client={playerClients}");
            psi.ArgumentList.Add("--no-playlist");
        }
        else
        {
            if (InstagramSessionStore.HasSavedSession)
            {
                psi.ArgumentList.Add("--cookies");
                psi.ArgumentList.Add(InstagramSessionStore.CookieFilePath);
                progress?.Report("Using signed-in Instagram session…");
            }
        }

        psi.ArgumentList.Add("--newline");
        psi.ArgumentList.Add("--no-mtime");
        psi.ArgumentList.Add("--write-info-json");
        psi.ArgumentList.Add("--retries");
        psi.ArgumentList.Add("3");
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add(outputTemplate);
        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add(url);

        using var proc = Process.Start(psi)
                         ?? throw new InvalidOperationException("Failed to start yt-dlp.");

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var stdoutTask = DrainAsync(proc.StandardOutput, progress, stdout, cancellationToken);
        var stderrTask = DrainAsync(proc.StandardError, progress, stderr, cancellationToken);

        await using (cancellationToken.Register(() =>
                     {
                         try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
                     }))
        {
            await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }

        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        return (proc.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static bool LooksLikeHttp403(string stdout, string stderr)
    {
        var blob = (stdout + "\n" + stderr).ToLowerInvariant();
        return blob.Contains("http error 403")
               || blob.Contains("403: forbidden")
               || blob.Contains("unable to download video data");
    }

    private static void AugmentPath(ProcessStartInfo psi, params string?[] toolPaths)
    {
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var wingetLinks = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WinGet", "Links");
        if (Directory.Exists(wingetLinks))
            dirs.Add(wingetLinks);

        foreach (var tool in toolPaths)
        {
            if (string.IsNullOrWhiteSpace(tool)) continue;
            var dir = Path.GetDirectoryName(tool);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                dirs.Add(dir);
        }

        foreach (var bin in EnumerateWinGetPackageBins("yt-dlp.FFmpeg*", "ffmpeg.exe")
                     .Select(Path.GetDirectoryName)
                     .Where(d => !string.IsNullOrWhiteSpace(d)))
            dirs.Add(bin!);

        var current = psi.Environment["PATH"] ?? Environment.GetEnvironmentVariable("PATH") ?? "";
        var userPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
        var machinePath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) ?? "";
        var merged = string.Join(Path.PathSeparator,
            dirs.Concat(new[] { current, userPath, machinePath })
                .SelectMany(p => p.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase));
        psi.Environment["PATH"] = merged;
    }

    private static async Task DrainAsync(
        StreamReader reader,
        IProgress<string>? progress,
        StringBuilder capture,
        CancellationToken cancellationToken)
    {
        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line))
                continue;
            capture.AppendLine(line);
            if (line.Contains('%')
                || line.Contains("Downloading", StringComparison.OrdinalIgnoreCase)
                || line.Contains("Destination", StringComparison.OrdinalIgnoreCase)
                || line.Contains("Merging", StringComparison.OrdinalIgnoreCase)
                || line.Contains("ERROR", StringComparison.OrdinalIgnoreCase)
                || line.Contains("WARNING", StringComparison.OrdinalIgnoreCase))
                progress?.Report(line.Trim());
        }
    }

    private static bool IsMediaExtension(string ext) =>
        ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".mkv", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".webm", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".m4a", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".mov", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".webp", StringComparison.OrdinalIgnoreCase);

    private static string? ResolveTool(string[] names, IEnumerable<string> candidates)
    {
        foreach (var name in names)
        {
            var onPath = FindOnPath(name);
            if (onPath is not null)
                return onPath;
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    private static IEnumerable<string> EnumerateWinGetPackageBins(string packageGlob, string exeName)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WinGet", "Packages");
        if (!Directory.Exists(root))
            yield break;

        string[] dirs;
        try { dirs = Directory.GetDirectories(root, packageGlob); }
        catch { yield break; }

        foreach (var dir in dirs)
        {
            string[] hits;
            try { hits = Directory.GetFiles(dir, exeName, SearchOption.AllDirectories); }
            catch { continue; }

            foreach (var hit in hits)
                yield return hit;
        }
    }

    private static string? FindOnPath(string fileName)
    {
        var combined = string.Join(Path.PathSeparator, new[]
        {
            Environment.GetEnvironmentVariable("PATH") ?? "",
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "",
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) ?? ""
        });

        foreach (var dir in combined.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var full = Path.Combine(dir.Trim().Trim('"'), fileName);
                if (File.Exists(full)) return full;
            }
            catch
            {
                // ignore
            }
        }

        return null;
    }

    private static string Tail(string text, int maxChars)
    {
        text = text.Trim();
        if (text.Length <= maxChars) return text;
        return "…" + text[^maxChars..];
    }

    private static string FormatYtDlpFailure(RemoteMediaPlatform platform, int exitCode, string detail, bool isClipDownload = false)
    {
        var lower = detail.ToLowerInvariant();

        if (isClipDownload)
        {
            if (lower.Contains("download sections")
                || lower.Contains("section")
                || lower.Contains("invalid")
                || lower.Contains("cannot fit"))
            {
                return "This segment couldn't be downloaded. Try widening the selection or updating yt-dlp (winget upgrade yt-dlp).\n" + detail;
            }
        }

        if (platform == RemoteMediaPlatform.Instagram)
        {
            if (lower.Contains("empty media response")
                || lower.Contains("not granting access")
                || lower.Contains("login required")
                || lower.Contains("cookies"))
            {
                return "Instagram blocked this post. Sign in under Settings → Instagram account (or Analyze → Sign in to Instagram), then retry. Private posts require the account that can view them.\n" + detail;
            }
        }

        if (lower.Contains("members-only")
            || lower.Contains("members only")
            || lower.Contains("join this channel")
            || lower.Contains("premium")
            || lower.Contains("this video is available to this channel's members"))
        {
            return "This video is members-only or Premium-exclusive and isn't available to the signed-in account. Sign in (Settings → YouTube account) with an account that can watch it, or pick a public URL.";
        }

        if (lower.Contains("sign in to confirm")
            || lower.Contains("login required")
            || lower.Contains("age-restricted")
            || lower.Contains("confirm your age"))
        {
            return "YouTube blocked this URL (age gate / sign-in). Sign in under Settings → YouTube account, then retry. Public videos usually work without signing in.";
        }

        if (lower.Contains("private video") || lower.Contains("video unavailable"))
        {
            return "That video is private or unavailable. Use a public watch URL.";
        }

        if (lower.Contains("http error 403")
            || lower.Contains("403: forbidden")
            || lower.Contains("unable to download video data"))
        {
            return "YouTube blocked the media URL (HTTP 403). Update yt-dlp (winget upgrade yt-dlp), try Sign in to YouTube in Settings, then retry Stream & analyze.\n" + detail;
        }

        return $"yt-dlp failed (exit {exitCode}).{(string.IsNullOrWhiteSpace(detail) ? "" : "\n" + detail)}";
    }
}