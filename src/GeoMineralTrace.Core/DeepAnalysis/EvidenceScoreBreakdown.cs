namespace GeoMineralTrace.Core.DeepAnalysis;

/// <summary>
/// Per-dimension Deep Analysis scores (each 0–1) plus the combined confidence.
/// </summary>
/// <remarks>
/// <para><b>Combined formula</b> (weights from <see cref="DeepAnalysisOptions"/>, renormalized):</para>
/// <code>
/// combined =
///   wSR  * SourceReliability +
///   wC   * Corroboration +
///   wS   * Specificity +
///   wCon * Consistency +
///   wP   * Plausibility
/// </code>
/// <para>Defaults: wSR=0.20, wC=0.35, wS=0.20, wCon=0.15, wP=0.10.</para>
/// </remarks>
public sealed record EvidenceScoreBreakdown(
    double SourceReliability,
    double Corroboration,
    double Specificity,
    double Consistency,
    double Plausibility,
    double Combined,
    string? Notes = null);
