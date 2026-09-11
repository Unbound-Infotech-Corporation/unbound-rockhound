using System.Text.RegularExpressions;
using GeoMineralTrace.Core.Common;
using GeoMineralTrace.Pipeline.Abstractions;

namespace GeoMineralTrace.Pipeline.Audio;

/// <summary>
/// Offline transcription via sidecar .srt / .vtt / .txt next to the media file.
/// Hook point for Whisper/Windows Speech adapters without network.
/// </summary>
public sealed class SidecarTranscriptionEngine : ITranscriptionEngine
{
    public string EngineName => "Sidecar transcript (.srt/.vtt/.txt)";

    public async Task<TranscriptionResult> TranscribeAsync(
        string mediaPath,
        CancellationToken cancellationToken = default)
    {
        foreach (var path in SidecarPaths(mediaPath))
        {
            if (!File.Exists(path)) continue;
            var raw = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            if (path.EndsWith(".srt", StringComparison.OrdinalIgnoreCase))
                return ParseSrt(raw);
            if (path.EndsWith(".vtt", StringComparison.OrdinalIgnoreCase))
                return ParseVtt(raw);

            return new TranscriptionResult(
                raw.Trim(),
                Ocr.SidecarOcrEngine.DetectLanguageHint(raw),
                [new TranscriptSegment(TimeSpan.Zero, TimeSpan.Zero, raw.Trim())],
                Confidence.Medium,
                $"Loaded plain transcript: {Path.GetFileName(path)}");
        }

        return new TranscriptionResult(
            string.Empty,
            null,
            [],
            Confidence.None,
            "No sidecar transcript found. Place media.srt / media.vtt / media.txt beside the video, or plug in an ASR engine.");
    }

    private static IEnumerable<string> SidecarPaths(string mediaPath)
    {
        yield return Path.ChangeExtension(mediaPath, ".srt");
        yield return Path.ChangeExtension(mediaPath, ".vtt");
        yield return Path.ChangeExtension(mediaPath, ".txt");
        yield return mediaPath + ".srt";
        yield return mediaPath + ".txt";
    }

    public static TranscriptionResult ParseSrt(string raw)
    {
        var segments = new List<TranscriptSegment>();
        var blocks = Regex.Split(raw.Replace("\r\n", "\n"), @"\n\s*\n");
        foreach (var block in blocks)
        {
            var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length < 2) continue;
            var timeLine = lines.FirstOrDefault(l => l.Contains("-->"));
            if (timeLine is null) continue;
            var parts = timeLine.Split("-->", StringSplitOptions.TrimEntries);
            if (parts.Length != 2) continue;
            if (!TryParseSrtTime(parts[0], out var start) || !TryParseSrtTime(parts[1], out var end))
                continue;
            var text = string.Join(' ', lines.SkipWhile(l => !l.Contains("-->")).Skip(1));
            if (!string.IsNullOrWhiteSpace(text))
                segments.Add(new TranscriptSegment(start, end, text));
        }

        var full = string.Join(' ', segments.Select(s => s.Text));
        return new TranscriptionResult(full, Ocr.SidecarOcrEngine.DetectLanguageHint(full), segments,
            segments.Count > 0 ? Confidence.High : Confidence.Low,
            "Parsed SubRip (.srt) sidecar.");
    }

    public static TranscriptionResult ParseVtt(string raw)
    {
        // Strip WEBVTT header then reuse SRT-like timing with '.' millis
        var normalized = raw.Replace(".", ",");
        var result = ParseSrt(normalized);
        return result with { Notes = "Parsed WebVTT sidecar." };
    }

    private static bool TryParseSrtTime(string value, out TimeSpan ts)
    {
        ts = default;
        value = value.Trim().Replace(',', '.');
        // HH:MM:SS.mmm or MM:SS.mmm
        var m = Regex.Match(value, @"^(?:(?<h>\d+):)?(?<m>\d+):(?<s>\d+)(?:\.(?<ms>\d+))?$");
        if (!m.Success) return false;
        var h = m.Groups["h"].Success ? int.Parse(m.Groups["h"].Value) : 0;
        var min = int.Parse(m.Groups["m"].Value);
        var s = int.Parse(m.Groups["s"].Value);
        var ms = m.Groups["ms"].Success ? int.Parse(m.Groups["ms"].Value.PadRight(3, '0')[..3]) : 0;
        ts = new TimeSpan(0, h, min, s, ms);
        return true;
    }
}
