using GeoMineralTrace.Core.Evidence;
using GeoMineralTrace.Core.Solar;

namespace GeoMineralTrace.Solar;

/// <summary>
/// Computes inverse solar locus from shadow-measurement evidence when present.
/// Used by the analysis pipeline and re-fusion (RefuseAsync) so solar is not manual-only.
/// </summary>
public static class SolarLocusFromEvidence
{
    public static SolarLocusResult? TryComputeLocus(
        Guid sessionId,
        IReadOnlyList<EvidenceItem> evidence,
        SolarLocusEngine? engine = null,
        LocusSearchBounds? bounds = null,
        DateTimeOffset? defaultObservationUtc = null)
    {
        var measurements = ShadowMeasurementParser.ParseFromEvidence(sessionId, evidence, defaultObservationUtc);
        if (measurements.Count == 0)
            return null;

        engine ??= new SolarLocusEngine();
        var searchBounds = bounds ?? LocusSearchBounds.ContiguousUnitedStates;
        return engine.ComputeLocus(sessionId, measurements, searchBounds);
    }
}
