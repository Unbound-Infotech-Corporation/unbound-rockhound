namespace GeoMineralTrace.Core.Analysis;

/// <summary>
/// One video analysis run (single file or batch member).
/// Each submission gets a distinct session ID used as the case/history record.
/// </summary>
public sealed class AnalysisSession
{
    public required Guid Id { get; init; }
    public required string DisplayName { get; init; }
    public IReadOnlyList<string> SourceMediaPaths { get; init; } = [];
    public string? SourceUrl { get; set; }
    public AnalysisSourceKind SourceKind { get; set; } = AnalysisSourceKind.LocalFile;
    public AnalysisStatus Status { get; set; } = AnalysisStatus.Created;
    public double ProgressFraction { get; set; }
    public string? CurrentStage { get; set; }
    public string? ErrorMessage { get; set; }
    /// <summary>Human-readable reason when fusion produced zero location hypotheses.</summary>
    public string? FusionEmptyReason { get; set; }
    public int EvidenceCount { get; set; }
    public int HypothesisCount { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public CancellationTokenSource? Cancellation { get; set; }
}

public enum AnalysisStatus
{
    Created = 0,
    Queued = 1,
    Running = 2,
    Cancelling = 3,
    Cancelled = 4,
    Completed = 5,
    Failed = 6
}

public enum AnalysisSourceKind
{
    LocalFile = 0,
    YouTube = 1,
    Instagram = 2,
    ScreenCapture = 3,
    Sample = 4,
    Batch = 5
}
