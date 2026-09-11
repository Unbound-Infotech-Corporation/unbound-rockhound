using System.Diagnostics;
using System.Text;
using GeoMineralTrace.Core.App;
using GeoMineralTrace.Core.Common;
using GeoMineralTrace.Pipeline.Abstractions;

namespace GeoMineralTrace.Pipeline.Audio;

/// <summary>
/// Optional offline ASR via OpenAI Whisper CLI / whisper.cpp when installed on PATH.
/// Also consumes pre-generated <c>media.whisper.srt</c> / <c>media.asr.srt</c> sidecars
/// (no model weights are bundled with Unbound Rockhound).
/// </summary>
public sealed class WhisperCliTranscriptionEngine : ITranscriptionEngine
{
    public string EngineName => "Whisper CLI / .whisper.srt sidecar";

    public async Task<TranscriptionResult> TranscribeAsync(
        string mediaPath,
        CancellationToken cancellationToken = default)
    {
        foreach (var sidecar in WhisperSidecarPaths(mediaPath))
        {
            if (!File.Exists(sidecar)) continue;
            var raw = await File.ReadAllTextAsync(sidecar, cancellationToken).ConfigureAwait(false);
            var parsed = SidecarTranscriptionEngine.ParseSrt(raw);
            return parsed with
            {
                Notes = $"Loaded Whisper sidecar: {Path.GetFileName(sidecar)}"
            };
        }

        var whisper = ResolveWhisper();
        if (whisper is null)
        {
            return new TranscriptionResult(
                string.Empty,
                null,
                [],
                Confidence.None,
                "Whisper CLI not on PATH. Install openai-whisper or whisper.cpp, or place media.whisper.srt beside the video.");
        }

        var workDir = Path.Combine(
            Path.GetTempPath(),
            AppDataPaths.FolderName,
            "whisper",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);

        try
        {
            var stem = Path.GetFileNameWithoutExtension(mediaPath);
            var args = BuildArgs(whisper, mediaPath, workDir);
            var (exit, stdout, stderr) = await RunAsync(whisper.Path, args, workDir, cancellationToken)
                .ConfigureAwait(false);

            var srt = Directory.EnumerateFiles(workDir, "*.srt", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            // openai-whisper writes next to media by default when -o not honored — also check beside media
            srt ??= Path.ChangeExtension(mediaPath, ".srt");
            if (!File.Exists(srt))
                srt = Path.Combine(workDir, stem + ".srt");

            if (File.Exists(srt))
            {
                // Persist beside media for reuse
                var dest = Path.ChangeExtension(mediaPath, ".whisper.srt");
                try { File.Copy(srt, dest, overwrite: true); } catch { /* ignore */ }

                var raw = await File.ReadAllTextAsync(srt, cancellationToken).ConfigureAwait(false);
                var parsed = SidecarTranscriptionEngine.ParseSrt(raw);
                return parsed with
                {
                    Confidence = Confidence.High,
                    Notes = $"Whisper CLI ({whisper.Kind}) exit={exit}"
                };
            }

            return new TranscriptionResult(
                string.Empty,
                null,
                [],
                Confidence.None,
                $"Whisper ran but produced no SRT. exit={exit} stderr={Trim(stderr)} stdout={Trim(stdout)}");
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch { /* ignore */ }
        }
    }

    public static WhisperTool? ResolveWhisper()
    {
        foreach (var (name, kind) in new[]
                 {
                     ("whisper", "openai-whisper"),
                     ("whisper-cli", "whisper.cpp"),
                     ("whisper.cpp", "whisper.cpp"),
                     ("main", "whisper.cpp-main")
                 })
        {
            var path = FindOnPath(name);
            if (path is not null)
            {
                // Avoid false positive on unrelated "main.exe"
                if (kind == "whisper.cpp-main" &&
                    !path.Contains("whisper", StringComparison.OrdinalIgnoreCase))
                    continue;
                return new WhisperTool(path, kind);
            }
        }

        return null;
    }

    private static string BuildArgs(WhisperTool tool, string mediaPath, string workDir)
    {
        var quotedMedia = Quote(mediaPath);
        var quotedOut = Quote(workDir);
        return tool.Kind switch
        {
            "openai-whisper" =>
                $"{quotedMedia} --model tiny --output_format srt --output_dir {quotedOut} --verbose False",
            _ =>
                // whisper.cpp style — best-effort; users may need -m model path in their wrapper script
                $"-f {quotedMedia} -of {Quote(Path.Combine(workDir, "out"))} -osrt"
        };
    }

    private static IEnumerable<string> WhisperSidecarPaths(string mediaPath)
    {
        yield return Path.ChangeExtension(mediaPath, ".whisper.srt");
        yield return Path.ChangeExtension(mediaPath, ".asr.srt");
        yield return mediaPath + ".whisper.srt";
        yield return mediaPath + ".asr.srt";
    }

    private static string? FindOnPath(string exe)
    {
        var exts = OperatingSystem.IsWindows()
            ? new[] { ".exe", ".cmd", ".bat", "" }
            : new[] { "" };
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var dir in paths)
        {
            foreach (var ext in exts)
            {
                var candidate = Path.Combine(dir, exe + ext);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static async Task<(int Exit, string StdOut, string StdErr)> RunAsync(
        string fileName,
        string args,
        string workDir,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi)
                         ?? throw new InvalidOperationException($"Failed to start {fileName}");
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // Cap Whisper so a stuck CLI cannot freeze analysis indefinitely.
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(8));
        await proc.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        return (proc.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static string Quote(string path) =>
        path.Contains(' ') ? $"\"{path}\"" : path;

    private static string Trim(string s) =>
        s.Length <= 200 ? s.Trim() : s.Trim()[..200] + "…";

    public sealed record WhisperTool(string Path, string Kind);
}