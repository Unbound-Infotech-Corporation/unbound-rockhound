using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GeoMineralTrace.Core.App;
using GeoMineralTrace.Core.Evidence;
using GeoMineralTrace.Core.Hypothesis;

namespace GeoMineralTrace.Pipeline.Diagnostics;

/// <summary>
/// Temporary per-analysis diagnostic log for handoff debugging.
/// Read-only instrumentation — does not affect pipeline/fusion/UI behavior.
/// </summary>
public sealed class AnalysisDiagnosticLog : IDisposable
{
    private static readonly ConcurrentDictionary<Guid, string> PathsBySession = new();
    private static string? _latestPath;
    private readonly object _gate = new();
    private readonly StreamWriter _writer;
    private bool _disposed;

    public Guid SessionId { get; }
    public string FilePath { get; }
    public DateTimeOffset StartedAtUtc { get; }

    public static string RootDirectory => AppDataPaths.Sub("diagnostics");

    public static string? LatestLogPath => _latestPath;

    public static string? TryGetPath(Guid sessionId) =>
        PathsBySession.TryGetValue(sessionId, out var p) ? p : null;

    public static AnalysisDiagnosticLog Begin(Guid sessionId)
    {
        Directory.CreateDirectory(RootDirectory);
        var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var path = Path.Combine(RootDirectory, $"analysis-{sessionId:N}-{stamp}.log");
        var log = new AnalysisDiagnosticLog(sessionId, path);
        PathsBySession[sessionId] = path;
        _latestPath = path;
        try
        {
            File.WriteAllText(Path.Combine(RootDirectory, "latest.txt"), path + Environment.NewLine);
        }
        catch
        {
            // best-effort pointer only
        }

        return log;
    }

    /// <summary>Append to an existing session log after the pipeline writer is closed.</summary>
    public static void AppendToSession(Guid sessionId, Action<TextWriter> write)
    {
        if (!PathsBySession.TryGetValue(sessionId, out var path) || !File.Exists(path))
            return;

        try
        {
            lock (PathsBySession)
            {
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
                write(writer);
                writer.Flush();
            }
        }
        catch
        {
            // Diagnostics must never break the app.
        }
    }

    public static void AppendSection(Guid sessionId, string title, IEnumerable<string> lines)
    {
        AppendToSession(sessionId, w =>
        {
            w.WriteLine();
            w.WriteLine(new string('=', 72));
            w.WriteLine($"  {title}");
            w.WriteLine(new string('=', 72));
            foreach (var line in lines)
                w.WriteLine(line);
        });
    }

    private AnalysisDiagnosticLog(Guid sessionId, string path)
    {
        SessionId = sessionId;
        FilePath = path;
        StartedAtUtc = DateTimeOffset.UtcNow;
        var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
        Line($"GeoMineralTrace analysis diagnostic log");
        Line($"Created (local): {DateTimeOffset.Now:O}");
        Line($"File: {path}");
        Line($"NOTE: Temporary instrumentation — safe to delete this folder anytime.");
    }

    public void Section(string title)
    {
        lock (_gate)
        {
            _writer.WriteLine();
            _writer.WriteLine(new string('=', 72));
            _writer.WriteLine($"  {title}");
            _writer.WriteLine(new string('=', 72));
        }
    }

    public void Line(string text)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _writer.WriteLine(text);
        }
    }

    public void Blank() => Line("");

    public void KeyValue(string key, string? value) =>
        Line($"{key,-28} {value ?? "(null)"}");

    public void StageStart(string stage) =>
        Line($"[{NowStamp()}] START  {stage}");

    public void StageEnd(string stage, string summary) =>
        Line($"[{NowStamp()}] END    {stage} — {summary}");

    public void Skipped(string stage, string reason) =>
        Line($"[{NowStamp()}] SKIPPED/FALLBACK: {stage} — {reason}");

    public void Sample(string label, string? text, int maxChars = 100)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            Line($"  sample[{label}]: (empty)");
            return;
        }

        var trimmed = text.Replace("\r", " ").Replace("\n", " ").Trim();
        if (trimmed.Length > maxChars)
            trimmed = trimmed[..maxChars] + "…";
        Line($"  sample[{label}]: {trimmed}");
    }

    public void LogFileFingerprint(string label, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            Line($"{label}: (no path)");
            return;
        }

        Line($"{label} path: {path}");
        try
        {
            if (!File.Exists(path))
            {
                Line($"{label} exists: NO");
                return;
            }

            var info = new FileInfo(path);
            Line($"{label} size: {info.Length:N0} bytes ({info.Length / 1024.0:F1} KiB)");
            Line($"{label} modified: {info.LastWriteTimeUtc:O} (UTC)");
            Line($"{label} sha256: {ComputeContentFingerprint(path)}");
        }
        catch (Exception ex)
        {
            Line($"{label} fingerprint error: {ex.Message}");
        }
    }

    public void LogEvidenceInventory(
        IReadOnlyList<EvidenceItem> pipelineEvidence,
        IReadOnlyList<EvidenceItem> boardSnapshot,
        Guid sessionId)
    {
        Section("EVIDENCE");
        var sessionOnly = pipelineEvidence.Where(e => e.AnalysisSessionId == sessionId).ToList();
        var foreign = pipelineEvidence.Where(e => e.AnalysisSessionId != sessionId).ToList();
        var boardSession = boardSnapshot.Where(e => e.AnalysisSessionId == sessionId).ToList();
        var boardForeign = boardSnapshot.Where(e => e.AnalysisSessionId != sessionId).ToList();

        KeyValue("Pipeline evidence total", pipelineEvidence.Count.ToString(CultureInfo.InvariantCulture));
        KeyValue("Pipeline evidence (this session)", sessionOnly.Count.ToString(CultureInfo.InvariantCulture));
        KeyValue("Pipeline evidence (OTHER sessions)", foreign.Count.ToString(CultureInfo.InvariantCulture));
        KeyValue("In-memory board total", boardSnapshot.Count.ToString(CultureInfo.InvariantCulture));
        KeyValue("In-memory board (this session)", boardSession.Count.ToString(CultureInfo.InvariantCulture));
        KeyValue("In-memory board (OTHER sessions)", boardForeign.Count.ToString(CultureInfo.InvariantCulture));

        if (foreign.Count > 0 || boardForeign.Count > 0)
            Line("*** WARNING: evidence from other session IDs is present — possible leak ***");

        Line("--- Pipeline evidence rows (type | sessionId | summary) ---");
        var i = 0;
        foreach (var e in pipelineEvidence)
        {
            i++;
            var sid = e.AnalysisSessionId == sessionId ? "THIS" : e.AnalysisSessionId.ToString("N");
            Line($"  [{i:D3}] {e.Type,-22} session={sid} id={e.Id:N}  {TruncateOneLine(e.Summary, 80)}");
        }
    }

    public void LogFusion(
        bool invoked,
        bool optionsWouldSkip,
        string cacheStatus,
        IReadOnlyList<LocationHypothesis> hypotheses)
    {
        Section("FUSION");
        KeyValue("Fusion invoked", invoked ? "YES" : "NO");
        KeyValue("Options.RunFusion=false?", optionsWouldSkip ? "yes (would skip)" : "no");
        KeyValue("Cache", cacheStatus);
        KeyValue("Ranked hypotheses", hypotheses.Count.ToString(CultureInfo.InvariantCulture));

        var top = hypotheses.OrderBy(h => h.Rank).Take(3).ToList();
        if (top.Count == 0)
        {
            Line("Top hypotheses: (none)");
            return;
        }

        Line("--- Top hypotheses ---");
        foreach (var h in top)
        {
            var loc = h.Center is { } c
                ? $"lat={c.LatitudeDegrees:F5}, lon={c.LongitudeDegrees:F5}, r={h.RadiusKm?.ToString(CultureInfo.InvariantCulture) ?? "?"}km"
                : (h.RegionDescription ?? "(no coords — region/text only)");
            Line($"  #{h.Rank} conf={h.Confidence.Value:F3}  {h.Label}");
            Line($"       location: {loc}");
            Line($"       evidenceIds: {string.Join(", ", h.SupportingEvidenceIds.Select(id => id.ToString("N")))}");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                _writer.WriteLine();
                _writer.WriteLine($"[{NowStamp()}] pipeline writer closed (UI may still append below)");
                _writer.Flush();
                _writer.Dispose();
            }
            catch
            {
                // ignore
            }
        }
    }

    private static string NowStamp() =>
        DateTimeOffset.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

    private static string TruncateOneLine(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return "(empty)";
        var t = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return t.Length <= max ? t : t[..max] + "…";
    }

    /// <summary>
    /// Full-file SHA256 for small files; size-aware fingerprint for large videos
    /// (first 1 MiB + last 64 KiB + length) so stale-cache reuse is still obvious.
    /// </summary>
    public static string ComputeContentFingerprint(string path)
    {
        const long fullHashLimit = 32L * 1024 * 1024; // 32 MiB
        var info = new FileInfo(path);
        using var sha = SHA256.Create();

        if (info.Length <= fullHashLimit)
        {
            using var fs = File.OpenRead(path);
            var hash = sha.ComputeHash(fs);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        using (var fs = File.OpenRead(path))
        {
            var head = new byte[1024 * 1024];
            var read = fs.Read(head, 0, head.Length);
            sha.TransformBlock(head, 0, read, null, 0);

            var lenBytes = BitConverter.GetBytes(info.Length);
            sha.TransformBlock(lenBytes, 0, lenBytes.Length, null, 0);

            var tailLen = (int)Math.Min(64 * 1024, info.Length);
            fs.Seek(-tailLen, SeekOrigin.End);
            var tail = new byte[tailLen];
            var tRead = fs.Read(tail, 0, tail.Length);
            sha.TransformFinalBlock(tail, 0, tRead);
        }

        return "partial:" + Convert.ToHexString(sha.Hash!).ToLowerInvariant()
               + $" (len={info.Length})";
    }
}
