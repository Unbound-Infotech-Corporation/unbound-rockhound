namespace GeoMineralTrace.Core.DeepAnalysis;

/// <summary>
/// Tunable thresholds and dimension weights for the Deep Analysis pass.
/// Defaults are inspectable; change without a rebuild via DI / settings binding.
/// </summary>
public sealed class DeepAnalysisOptions
{
    /// <summary>Minimum combined item score to enter any cluster.</summary>
    public double MinimumItemConfidence { get; set; } = 0.25;

    /// <summary>
    /// Minimum cluster score (avg item confidence × modality count / 4, clamped)
    /// for a cluster to produce a hypothesis.
    /// </summary>
    public double MinimumClusterConfidence { get; set; } = 0.35;

    /// <summary>Maximum number of location clusters to surface as hypotheses.</summary>
    public int MaxClusters { get; set; } = 3;

    /// <summary>Max distance (km) for GPS cells / place centroids to merge into one cluster.</summary>
    public double ClusterMergeRadiusKm { get; set; } = 75;

    // --- Dimension weights (must sum ≈ 1.0; renormalized if they don't) ---

    /// <summary>Source reliability weight. Default 0.20.</summary>
    public double WeightSourceReliability { get; set; } = 0.20;

    /// <summary>Cross-modal corroboration weight. Default 0.35 (highest — best signal).</summary>
    public double WeightCorroboration { get; set; } = 0.35;

    /// <summary>Geographic / entity specificity weight. Default 0.20.</summary>
    public double WeightSpecificity { get; set; } = 0.20;

    /// <summary>Consistency with accepted context weight. Default 0.15.</summary>
    public double WeightConsistency { get; set; } = 0.15;

    /// <summary>Temporal/spatial plausibility weight. Default 0.10.</summary>
    public double WeightPlausibility { get; set; } = 0.10;

    public void NormalizeWeights()
    {
        var sum = WeightSourceReliability + WeightCorroboration + WeightSpecificity
                  + WeightConsistency + WeightPlausibility;
        if (sum <= 0 || Math.Abs(sum - 1.0) < 1e-6)
            return;
        WeightSourceReliability /= sum;
        WeightCorroboration /= sum;
        WeightSpecificity /= sum;
        WeightConsistency /= sum;
        WeightPlausibility /= sum;
    }
}
