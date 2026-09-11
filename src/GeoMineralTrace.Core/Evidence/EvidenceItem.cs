using GeoMineralTrace.Core.Common;
using GeoMineralTrace.Core.DeepAnalysis;

namespace GeoMineralTrace.Core.Evidence;

/// <summary>
/// Atomic unit of forensic evidence. Every clue carries provenance, confidence,
/// and optional linkage to a source media frame/timestamp.
/// </summary>
public sealed class EvidenceItem
{
    public required Guid Id { get; init; }
    public required Guid AnalysisSessionId { get; init; }
    public required EvidenceType Type { get; init; }
    public required string Summary { get; init; }
    public string? RawContent { get; init; }
    public string? SourceMediaPath { get; init; }
    public TimeSpan? MediaTimestamp { get; init; }
    public int? FrameIndex { get; init; }
    public string? PreviewAssetPath { get; init; }
    public required Confidence Confidence { get; init; }
    public string? Notes { get; init; }
    public string? DetectedLanguage { get; init; }
    public IReadOnlyDictionary<string, string>? Attributes { get; init; }
    public DateTimeOffset ExtractedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public EvidenceWeight UserWeight { get; set; } = EvidenceWeight.Neutral;
    public bool IsRejected { get; set; }

    /// <summary>Deep Analysis combined score (0–1), set after a deep pass.</summary>
    public double? DeepScore { get; set; }

    /// <summary>True when this item was selected into a corroborating cluster.</summary>
    public bool? DeepSelected { get; set; }

    /// <summary>Why selected / considered-not-selected (inspectable on Evidence Board).</summary>
    public string? DeepSelectionNote { get; set; }

    /// <summary>Per-dimension breakdown from the last Deep Analysis pass.</summary>
    public EvidenceScoreBreakdown? DeepBreakdown { get; set; }

    /// <summary>
    /// Effective weight after user accept/reject/re-weight decisions.
    /// Rejected evidence contributes zero to fusion.
    /// </summary>
    public double EffectiveWeight =>
        IsRejected
            ? 0.0
            : Confidence.Value * UserWeight switch
            {
                EvidenceWeight.Downweighted => 0.5,
                EvidenceWeight.Upweighted => 1.5,
                EvidenceWeight.Accepted => 2.0,
                _ => 1.0
            };
}

public enum EvidenceWeight
{
    Downweighted = -1,
    Neutral = 0,
    Upweighted = 1,
    Accepted = 2
}
