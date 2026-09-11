using System.Text;
using System.Text.RegularExpressions;
using GeoMineralTrace.Core.Common;
using GeoMineralTrace.Pipeline.Abstractions;

namespace GeoMineralTrace.Pipeline.Ocr;

/// <summary>
/// Pluggable OCR default: reads sidecar .txt/.ocr.txt next to frames, and
/// extracts uppercase sign-like tokens from any embedded plan notes.
/// Replace with Windows.Media.Ocr adapter in the App layer for real OCR.
/// </summary>
public sealed class SidecarOcrEngine : IOcrEngine
{
    public string EngineName => "Sidecar/Heuristic OCR";

    public async Task<IReadOnlyList<OcrResult>> RecognizeAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        var results = new List<OcrResult>();
        foreach (var sidecar in GetSidecarCandidates(imagePath))
        {
            if (!File.Exists(sidecar)) continue;
            var text = await File.ReadAllTextAsync(sidecar, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text)) continue;

            results.Add(new OcrResult(
                text.Trim(),
                Confidence.High,
                DetectLanguageHint(text),
                $"Loaded from sidecar: {Path.GetFileName(sidecar)}"));
        }

        if (results.Count == 0 && imagePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            var text = await File.ReadAllTextAsync(imagePath, cancellationToken).ConfigureAwait(false);
            results.Add(new OcrResult(text, Confidence.Low, "en", "Plan/text artifact — not visual OCR."));
        }

        return results;
    }

    private static IEnumerable<string> GetSidecarCandidates(string imagePath)
    {
        yield return imagePath + ".ocr.txt";
        yield return Path.ChangeExtension(imagePath, ".ocr.txt");
        yield return Path.ChangeExtension(imagePath, ".txt");
    }

    public static string? DetectLanguageHint(string text)
    {
        // Extremely lightweight heuristic — not a substitute for CLD/ICU
        if (Regex.IsMatch(text, @"[\u0400-\u04FF]")) return "ru";
        if (Regex.IsMatch(text, @"[\u4E00-\u9FFF]")) return "zh";
        if (Regex.IsMatch(text, @"[\u3040-\u30FF]")) return "ja";
        if (Regex.IsMatch(text, @"[äöüßÄÖÜ]")) return "de";
        if (Regex.IsMatch(text, @"[áéíóúñ¿¡]", RegexOptions.IgnoreCase)) return "es";
        return "en";
    }
}

/// <summary>
/// Pass-through engine used when the App injects Windows.Media.Ocr results.
/// </summary>
public sealed class DelegateOcrEngine : IOcrEngine
{
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<OcrResult>>> _fn;
    public string EngineName { get; }

    public DelegateOcrEngine(
        string name,
        Func<string, CancellationToken, Task<IReadOnlyList<OcrResult>>> fn)
    {
        EngineName = name;
        _fn = fn;
    }

    public Task<IReadOnlyList<OcrResult>> RecognizeAsync(string imagePath, CancellationToken cancellationToken = default) =>
        _fn(imagePath, cancellationToken);
}
