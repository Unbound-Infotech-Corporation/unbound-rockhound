namespace GeoMineralTrace.Core.Analysis;

/// <summary>
/// Lightweight index row for completed video analyses (references session data on disk).
/// </summary>
public sealed class AnalysisHistoryEntry
{
    public Guid SessionId { get; init; }
    public required string Title { get; init; }
    public string? SourceUrl { get; init; }
    public DateTimeOffset CompletedAtUtc { get; init; }
    public int HypothesisCount { get; init; }
    public Guid? LeadHypothesisId { get; init; }
    public string? LeadLabel { get; init; }
    public double? LeadConfidence { get; init; }
    public double? LeadLatitude { get; init; }
    public double? LeadLongitude { get; init; }
    public string? LeadReasoningSummary { get; init; }
}
