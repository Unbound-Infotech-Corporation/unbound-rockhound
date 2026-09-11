namespace GeoMineralTrace.Core.Common;

using GeoMineralTrace.Core.Evidence;

/// <summary>
/// Maps confidence + corroboration into a single prominence score [0, 1] for UI weighting.
/// Low scores should render muted; high scores bold and high-contrast.
/// </summary>
public static class ConfidenceProminence
{
    /// <summary>
    /// Corroboration multiplier — single-source hypotheses stay visually subdued.
    /// </summary>
    public static double CorroborationFactor(int supportingSourceCount) =>
        supportingSourceCount switch
        {
            <= 0 => 0.5,
            1 => 0.55,
            2 => 0.78,
            _ => 1.0
        };

    public static double FromConfidence(double confidenceValue, int supportingSourceCount = 1) =>
        Math.Clamp(Math.Clamp(confidenceValue, 0.0, 1.0) * CorroborationFactor(supportingSourceCount), 0.0, 1.0);

    public static double FromHypothesis(Confidence confidence, int supportingEvidenceCount) =>
        FromConfidence(confidence.Value, supportingEvidenceCount);

    /// <summary>
    /// Evidence prominence uses effective fusion weight, capped for display.
    /// </summary>
    public static double FromEvidence(EvidenceItem evidence)
    {
        if (evidence.IsRejected)
            return 0.12;

        // EffectiveWeight peaks around 2.0 * confidence when accepted.
        return Math.Clamp(evidence.EffectiveWeight / 1.5, 0.18, 1.0);
    }
}
