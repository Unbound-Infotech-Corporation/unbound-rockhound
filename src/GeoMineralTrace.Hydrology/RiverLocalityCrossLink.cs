using GeoMineralTrace.Core.Geo;
using GeoMineralTrace.Core.Hydrology;
using GeoMineralTrace.Core.Rockhounding;
using GeoMineralTrace.Hydrology.Geo;
using GeoMineralTrace.Hydrology.Storage;
using GeoMineralTrace.Rockhounding.Storage;

namespace GeoMineralTrace.Hydrology;

/// <summary>Cross-links rivers and collecting localities by corridor distance.</summary>
public sealed class RiverLocalityCrossLink
{
    public async Task<IReadOnlyList<Watercourse>> FindRiversNearLocalityAsync(
        RiverStore rivers,
        Locality locality,
        double corridorRadiusKm = 5.0,
        CancellationToken cancellationToken = default)
    {
        if (locality.Coordinates is not { } coord)
            return [];

        await rivers.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var candidates = await rivers.FindNearAsync(coord, corridorRadiusKm + 10, limit: 50, cancellationToken)
            .ConfigureAwait(false);

        return candidates
            .Where(w => w.Vertices.Count >= 2 &&
                        PolylineDistance.MinDistanceKm(coord, w.Vertices) <= corridorRadiusKm)
            .OrderBy(w => PolylineDistance.MinDistanceKm(coord, w.Vertices))
            .ToList();
    }

    public async Task<IReadOnlyList<Locality>> FindLocalitiesNearRiverAsync(
        RiverStore rivers,
        LocalityStore localities,
        Watercourse watercourse,
        double corridorRadiusKm = 5.0,
        int limit = 30,
        CancellationToken cancellationToken = default)
    {
        if (watercourse.Centroid is not { } centroid)
            return [];

        await localities.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var near = await localities.FindNearAsync(centroid, corridorRadiusKm + (watercourse.LengthKm ?? 0) / 2, cancellationToken)
            .ConfigureAwait(false);

        if (watercourse.Vertices.Count < 2)
            return near.Take(limit).ToList();

        return near
            .Where(l => l.Coordinates is { } c &&
                        PolylineDistance.MinDistanceKm(c, watercourse.Vertices) <= corridorRadiusKm)
            .OrderBy(l => PolylineDistance.MinDistanceKm(l.Coordinates!.Value, watercourse.Vertices))
            .Take(limit)
            .ToList();
    }
}
