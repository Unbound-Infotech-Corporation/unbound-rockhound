using GeoMineralTrace.Core.Common;
using GeoMineralTrace.Core.Evidence;
using GeoMineralTrace.Core.Geo;

namespace GeoMineralTrace.Pipeline.Abstractions;

public interface IOcrEngine
{
    string EngineName { get; }
    Task<IReadOnlyList<OcrResult>> RecognizeAsync(
        string imagePath,
        CancellationToken cancellationToken = default);
}

public sealed record OcrResult(
    string Text,
    Confidence Confidence,
    string? DetectedLanguage,
    string? Notes);

public interface ITranscriptionEngine
{
    string EngineName { get; }
    Task<TranscriptionResult> TranscribeAsync(
        string mediaPath,
        CancellationToken cancellationToken = default);
}

public sealed record TranscriptionResult(
    string FullText,
    string? DetectedLanguage,
    IReadOnlyList<TranscriptSegment> Segments,
    Confidence Confidence,
    string? Notes);

public sealed record TranscriptSegment(
    TimeSpan Start,
    TimeSpan End,
    string Text);

public interface IKeyframeExtractor
{
    Task<IReadOnlyList<KeyframeInfo>> ExtractAsync(
        string mediaPath,
        string outputDirectory,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record KeyframeInfo(
    string ImagePath,
    TimeSpan Timestamp,
    int FrameIndex,
    double QualityScore,
    string ExtractionMethod);

public interface ISceneTagger
{
    Task<IReadOnlyList<SceneTag>> TagAsync(
        string imagePath,
        CancellationToken cancellationToken = default);
}

public sealed record SceneTag(
    EvidenceType Type,
    string Label,
    Confidence Confidence,
    string? Notes);
