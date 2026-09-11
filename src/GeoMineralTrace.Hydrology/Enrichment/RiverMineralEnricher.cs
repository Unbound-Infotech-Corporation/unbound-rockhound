using GeoMineralTrace.Core.Hydrology;
using GeoMineralTrace.Core.Rockhounding;
using GeoMineralTrace.Hydrology.Geo;
using GeoMineralTrace.Hydrology.Storage;
using GeoMineralTrace.Rockhounding.Storage;

namespace GeoMineralTrace.Hydrology.Enrichment;

/// <summary>Links watercourses to minerals via proximity to locality records (hybrid enrichment pass).</summary>
public sealed class RiverMineralEnricher
{
    public sealed record EnrichResult(int WatercoursesScanned, int AssociationsAdded, int SkippedCuratedDup);

    public async Task<EnrichResult> EnrichFromLocalitiesAsync(
        RiverStore rivers,
        LocalityStore localities,
        double corridorRadiusKm = 3.0,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await rivers.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await localities.InitializeAsync(cancellationToken).ConfigureAwait(false);

        await rivers.DeleteInferredMineralsAsync(cancellationToken).ConfigureAwait(false);

        var watercourses = await rivers.ListAllForEnrichmentAsync(cancellationToken).ConfigureAwait(false);
        var added = 0;
        var skippedDup = 0;
        var i = 0;

        foreach (var wc in watercourses)
        {
            i++;
            if (i % 100 == 0)
                progress?.Report($"Proximity enrich: {i}/{watercourses.Count} rivers…");

            if (wc.Centroid is not { } centroid || wc.Vertices.Count < 2)
                continue;

            var bboxRadius = corridorRadiusKm + (wc.LengthKm ?? 0) / 2;
            bboxRadius = Math.Clamp(bboxRadius, corridorRadiusKm, corridorRadiusKm + 15);

            var near = await localities.FindNearAsync(centroid, bboxRadius, cancellationToken).ConfigureAwait(false);
            foreach (var locality in near)
            {
                if (locality.Coordinates is not { } locCoord)
                    continue;

                var dist = PolylineDistance.MinDistanceKm(locCoord, wc.Vertices);
                if (dist > corridorRadiusKm)
                    continue;

                foreach (var mineral in locality.ReportedMinerals)
                {
                    var normalized = mineral.Trim();
                    if (normalized.Length == 0)
                        continue;

                    if (await rivers.HasCuratedMineralAsync(wc.Id, normalized, cancellationToken).ConfigureAwait(false))
                    {
                        skippedDup++;
                        continue;
                    }

                    var confidence = dist switch
                    {
                        <= 1.0 => AssociationConfidence.High,
                        <= 2.0 => AssociationConfidence.Medium,
                        _ => AssociationConfidence.Low
                    };

                    if (locality.SourceDataset?.Contains("USGS", StringComparison.OrdinalIgnoreCase) == true &&
                        confidence == AssociationConfidence.High)
                        confidence = AssociationConfidence.Medium;

                    await rivers.UpsertMineralAsync(new RiverMineralAssociation
                    {
                        WatercourseId = wc.Id,
                        MineralName = normalized,
                        AssociationKind = MineralAssociationKind.ProximityInferred,
                        Confidence = confidence,
                        SourceLocalityId = locality.Id,
                        DistanceKm = Math.Round(dist, 2),
                        Notes = $"Near {locality.Name} ({locality.SourceDataset ?? "locality"})"
                    }, cancellationToken).ConfigureAwait(false);
                    added++;
                }
            }
        }

        progress?.Report($"Proximity enrichment: {added} inferred associations ({skippedDup} curated dupes skipped).");
        return new EnrichResult(watercourses.Count, added, skippedDup);
    }
}
