using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using GeoMineralTrace.Core.App;
using GeoMineralTrace_App.Helpers;

namespace GeoMineralTrace_App.Services;

/// <summary>
/// Captures errors and builds shareable diagnostic reports (local-first; user chooses to send).
/// </summary>
public static class DiagnosticReportCenter
{
    public const string DefaultSupportEmail = AppBranding.SupportEmail;

    private static readonly object Gate = new();
    private static string? _currentPage;
    private static string? _lastUserAction;
    private static readonly ConcurrentQueue<string> RecentLog = new();
    private const int RecentLogCapacity = 200;

    public static string ReportsDirectory => AppDataPaths.Sub("reports");

    public static string AppLogPath => AppDataPaths.Sub("app.log");

    public static string PendingCrashMarkerPath =>
        Path.Combine(ReportsDirectory, "pending-crash.json");

    public static void Initialize()
    {
        Directory.CreateDirectory(ReportsDirectory);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                RecordException(ex, "AppDomain.UnhandledException", fatal: true);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            RecordException(args.Exception, "TaskScheduler.UnobservedTaskException");
            args.SetObserved();
        };

        Log("DiagnosticReportCenter initialized");
    }

    public static void SetNavigationContext(string? pageName) =>
        _currentPage = pageName;

    public static void SetLastUserAction(string? action) =>
        _lastUserAction = action;

    public static void Log(string message)
    {
        var line = $"{DateTimeOffset.Now:O} {message}";
        RecentLog.Enqueue(line);
        while (RecentLog.Count > RecentLogCapacity && RecentLog.TryDequeue(out _))
        {
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AppLogPath)!);
            File.AppendAllText(AppLogPath, line + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
            // Diagnostics must never break the app.
        }
    }

    public static void LogError(Exception ex, string context) =>
        RecordException(ex, context, fatal: false);

    public static string RecordException(Exception ex, string source, bool fatal = false)
    {
        var reportId = Guid.NewGuid().ToString("N")[..12];
        var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var fileName = $"report-{stamp}-{reportId}.txt";
        var path = Path.Combine(ReportsDirectory, fileName);

        try
        {
            Directory.CreateDirectory(ReportsDirectory);
            var body = BuildReportBody(ex, source, reportId, userNotes: null);
            File.WriteAllText(path, body, Encoding.UTF8);

            if (fatal)
            {
                var marker = new PendingCrashMarker
                {
                    ReportId = reportId,
                    ReportPath = path,
                    OccurredUtc = DateTimeOffset.UtcNow,
                    Source = source,
                    Message = ex.Message
                };
                File.WriteAllText(PendingCrashMarkerPath, JsonSerializer.Serialize(marker));
            }

            Log($"Recorded diagnostic report {reportId} ({source}) → {path}");
        }
        catch
        {
            // Last-resort legacy log
            try
            {
                File.AppendAllText(
                    Path.Combine(Path.GetDirectoryName(AppLogPath)!, "crash.log"),
                    $"{DateTimeOffset.Now:o} [{source}] {ex}\n");
            }
            catch
            {
                // ignore
            }
        }

        return reportId;
    }

    public static PendingCrashMarker? TryGetPendingCrash() =>
        TryReadPendingCrash(PendingCrashMarkerPath);

    public static void ClearPendingCrash()
    {
        try
        {
            if (File.Exists(PendingCrashMarkerPath))
                File.Delete(PendingCrashMarkerPath);
        }
        catch
        {
            // ignore
        }
    }

    public static IReadOnlyList<ReportSummary> ListReports(int max = 30)
    {
        try
        {
            Directory.CreateDirectory(ReportsDirectory);
            return Directory.EnumerateFiles(ReportsDirectory, "report-*.txt")
                .OrderByDescending(p => File.GetLastWriteTimeUtc(p))
                .Take(max)
                .Select(p => new ReportSummary(
                    Path.GetFileNameWithoutExtension(p),
                    p,
                    File.GetLastWriteTimeUtc(p),
                    File.ReadLines(p).FirstOrDefault() ?? "Diagnostic report"))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public static string? FindReportPath(string reportId)
    {
        try
        {
            return Directory.EnumerateFiles(ReportsDirectory, $"report-*-{reportId}.txt").FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    public static string BuildReportBody(
        Exception ex,
        string source,
        string reportId,
        string? userNotes)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{AppBranding.ProductName} — Diagnostic Report");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine($"Report ID:     {reportId}");
        sb.AppendLine($"Generated:     {DateTimeOffset.Now:O}");
        sb.AppendLine($"Source:        {source}");
        sb.AppendLine($"App version:   {GetAppVersion()}");
        sb.AppendLine($"OS:            {RuntimeInformation.OSDescription}");
        sb.AppendLine($"Framework:     {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"64-bit OS:     {Environment.Is64BitOperatingSystem}");
        sb.AppendLine($"Culture:       {CultureInfo.CurrentCulture.Name}");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(userNotes))
        {
            sb.AppendLine("--- User notes ---");
            sb.AppendLine(userNotes.Trim());
            sb.AppendLine();
        }

        sb.AppendLine("--- Context ---");
        sb.AppendLine($"Current page:  {_currentPage ?? "(unknown)"}");
        sb.AppendLine($"Last action:   {_lastUserAction ?? "(none recorded)"}");
        sb.AppendLine($"Online enrich: {AppPreferences.OnlineEnrichmentAllowed}");
        sb.AppendLine();

        AppendException(sb, ex);

        sb.AppendLine("--- Recent in-memory log ---");
        foreach (var line in RecentLog)
            sb.AppendLine(line);
        sb.AppendLine();

        sb.AppendLine("--- App log tail (last 80 lines) ---");
        sb.AppendLine(ReadLogTail(AppLogPath, 80));
        sb.AppendLine();

        sb.AppendLine("--- End of report ---");
        sb.AppendLine($"Send this file to the {AppBranding.ProductName} support contact.");
        return sb.ToString();
    }

    public static string AppendUserNotesToReport(string reportPath, string userNotes)
    {
        if (!File.Exists(reportPath))
            return BuildReportBody(new InvalidOperationException("Report file missing"), "manual", Guid.NewGuid().ToString("N")[..12], userNotes);

        var existing = File.ReadAllText(reportPath);
        if (existing.Contains("--- User notes ---"))
            return existing;

        var insertAt = existing.IndexOf("--- Context ---", StringComparison.Ordinal);
        if (insertAt < 0)
            return existing + Environment.NewLine + "--- User notes ---" + Environment.NewLine + userNotes;

        var sb = new StringBuilder();
        sb.Append(existing.AsSpan(0, insertAt));
        sb.AppendLine("--- User notes ---");
        sb.AppendLine(userNotes.Trim());
        sb.AppendLine();
        sb.Append(existing.AsSpan(insertAt));
        var updated = sb.ToString();
        File.WriteAllText(reportPath, updated, Encoding.UTF8);
        return updated;
    }

    public static async Task<string> ExportReportBundleAsync(string reportPath, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(ReportsDirectory);
        var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var zipPath = Path.Combine(ReportsDirectory, $"diagnostic-bundle-{stamp}.zip");

        await using var zipStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);

        if (File.Exists(reportPath))
        {
            var entry = archive.CreateEntry(Path.GetFileName(reportPath));
            await using var entryStream = entry.Open();
            await using var src = File.OpenRead(reportPath);
            await src.CopyToAsync(entryStream, cancellationToken).ConfigureAwait(false);
        }

        if (File.Exists(AppLogPath))
            await AddFileToZipAsync(archive, AppLogPath, "app.log", cancellationToken).ConfigureAwait(false);

        var analysisDiag = GeoMineralTrace.Pipeline.Diagnostics.AnalysisDiagnosticLog.RootDirectory;
        if (Directory.Exists(analysisDiag))
        {
            foreach (var log in Directory.EnumerateFiles(analysisDiag, "*.log")
                         .OrderByDescending(File.GetLastWriteTimeUtc)
                         .Take(3))
            {
                await AddFileToZipAsync(archive, log, $"analysis/{Path.GetFileName(log)}", cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return zipPath;
    }

    public static string BuildMailtoUri(string reportPath, string? userNotes)
    {
        var support = string.IsNullOrWhiteSpace(AppPreferences.SupportReportEmail)
            ? DefaultSupportEmail
            : AppPreferences.SupportReportEmail.Trim();

        var subject = Uri.EscapeDataString($"{AppBranding.ProductName} diagnostic report — {Path.GetFileName(reportPath)}");
        var preview = File.Exists(reportPath)
            ? File.ReadAllText(reportPath)
            : "(report file not found)";

        if (preview.Length > 1500)
            preview = preview[..1500] + "\n…(attach full report file or zip bundle)";

        var body = new StringBuilder();
        body.AppendLine($"{AppBranding.ProductName} error report");
        body.AppendLine();
        if (!string.IsNullOrWhiteSpace(userNotes))
        {
            body.AppendLine("What I was doing:");
            body.AppendLine(userNotes.Trim());
            body.AppendLine();
        }

        body.AppendLine("Please attach the saved report .txt or .zip from:");
        body.AppendLine(reportPath);
        body.AppendLine();
        body.AppendLine("Preview:");
        body.AppendLine(preview);

        var mailto = $"mailto:{support}?subject={subject}&body={Uri.EscapeDataString(body.ToString())}";
        return mailto.Length > 8000 ? $"mailto:{support}?subject={subject}" : mailto;
    }

    private static async Task AddFileToZipAsync(
        ZipArchive archive,
        string sourcePath,
        string entryName,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName);
        await using var entryStream = entry.Open();
        await using var src = File.OpenRead(sourcePath);
        await src.CopyToAsync(entryStream, cancellationToken).ConfigureAwait(false);
    }

    private static void AppendException(StringBuilder sb, Exception ex)
    {
        sb.AppendLine("--- Exception ---");
        for (Exception? cursor = ex; cursor is not null; cursor = cursor.InnerException)
        {
            sb.AppendLine($"Type:    {cursor.GetType().FullName}");
            sb.AppendLine($"Message: {cursor.Message}");
            if (!string.IsNullOrWhiteSpace(cursor.StackTrace))
            {
                sb.AppendLine("Stack:");
                sb.AppendLine(cursor.StackTrace);
            }

            sb.AppendLine();
        }
    }

    private static string ReadLogTail(string path, int maxLines)
    {
        if (!File.Exists(path))
            return "(no app.log yet)";

        try
        {
            var lines = File.ReadLines(path).TakeLast(maxLines).ToList();
            return lines.Count == 0 ? "(empty)" : string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex)
        {
            return $"(could not read log: {ex.Message})";
        }
    }

    private static string GetAppVersion()
    {
        var asm = Assembly.GetExecutingAssembly().GetName();
        return $"{asm.Name} {asm.Version?.ToString() ?? "unknown"}";
    }

    private static PendingCrashMarker? TryReadPendingCrash(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            return JsonSerializer.Deserialize<PendingCrashMarker>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    public sealed record ReportSummary(string Id, string Path, DateTime LastWriteUtc, string Title);

    public sealed class PendingCrashMarker
    {
        public string ReportId { get; set; } = "";
        public string ReportPath { get; set; } = "";
        public DateTimeOffset OccurredUtc { get; set; }
        public string Source { get; set; } = "";
        public string Message { get; set; } = "";
    }
}
