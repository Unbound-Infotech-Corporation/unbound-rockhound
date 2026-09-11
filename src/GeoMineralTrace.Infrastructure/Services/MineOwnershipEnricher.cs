using GeoMineralTrace.Claims.Storage;
using GeoMineralTrace.Core.Rockhounding;
using GeoMineralTrace.Rockhounding.Ownership;
using GeoMineralTrace.Rockhounding.Storage;

namespace GeoMineralTrace.Infrastructure.Services;

/// <summary>
/// Offline ownership enrichment for the mine/locality registry:
/// text heuristics + proximity to active BLM claims → LandType.
/// Does not download network data; uses local SQLite only.
/// </summary>
public sealed class MineOwnershipEnricher
{
    private const double ClaimProximityKm = 2.5;
    private const double CellDegrees = 0.05; // ~5.5 km

    private readonly LocalityStore _localities;
    private readonly ClaimStore _claims;

    public MineOwnershipEnricher(LocalityStore localities, ClaimStore claims)
    {
        _localities = localities;
        _claims = claims;
    }

    public async Task<MineOwnershipEnrichmentResult> EnrichAsync(
        bool overwriteKnown = false,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await _localities.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _claims.InitializeAsync(cancellationToken).ConfigureAwait(false);

        progress?.Report("Loading active BLM claim coordinates…");
        var claimPoints = await _claims.ListActiveCoordinateIndexAsync(cancellationToken).ConfigureAwait(false);
        var claimGrid = BuildGrid(claimPoints);
        progress?.Report($"Indexed {claimPoints.Count:N0} active/expiring claim points.");

        progress?.Report("Scanning localities…");
        var rows = await _localities.ListOwnershipScanRowsAsync(cancellationToken).ConfigureAwait(false);

        await _localities.BeginBulkWriteAsync(cancellationToken).ConfigureAwait(false);
        var hintPrivate = 0;
        var hintPublic = 0;
        var claimTagged = 0;
        var skipped = 0;
        var updated = 0;
        var scanned = 0;

        try
        {
            foreach (var row in rows)
            {
                scanned++;
                if (scanned % 25_000 == 0)
                    progress?.Report($"  scanned {scanned:N0} / {rows.Count:N0}…");

                if (!overwriteKnown && row.LandType != LandType.Unknown)
                {
                    skipped++;
                    continue;
                }

                var inferred = MineOwnershipHints.InferFromText(row.Name, row.AccessNotes, row.EnrichmentSummary);
                LandType next;
                string? note;

                if (inferred is LandType.Private)
                {
                    next = LandType.Private;
                    note = "Ownership hint: private/fee-mine language in name or notes (verify before visit)";
                    hintPrivate++;
                }
                else if (MineOwnershipHints.IsPublicSurface(inferred))
                {
                    next = inferred;
                    note = $"Ownership hint: {MineOwnershipHints.DisplayLabel(inferred)} language in name or notes (verify before visit)";
                    hintPublic++;
                }
                else if (NearClaim(claimGrid, row.Latitude, row.Longitude))
                {
                    next = LandType.Claim;
                    note = $"Near active BLM mining claim (≤{ClaimProximityKm:0.#} km) — not open collecting without permission";
                    claimTagged++;
                }
                else
                {
                    skipped++;
                    continue;
                }

                if (next == row.LandType && !overwriteKnown)
                {
                    skipped++;
                    continue;
                }

                await _localities.PatchLandTypeAsync(row.Id, next, note, cancellationToken).ConfigureAwait(false);
                updated++;
            }
        }
        finally
        {
            await _localities.EndBulkWriteAsync(cancellationToken).ConfigureAwait(false);
        }

        progress?.Report($"Done. Updated {updated:N0} rows.");
        return new MineOwnershipEnrichmentResult(scanned, updated, hintPrivate, hintPublic, claimTagged, skipped);
    }

    private static Dictionary<(int, int), List<(double Lat, double Lon)>> BuildGrid(
        IReadOnlyList<(double Latitude, double Longitude)> points)
    {
        var grid = new Dictionary<(int, int), List<(double, double)>>();
        foreach (var (lat, lon) in points)
        {
            var key = CellKey(lat, lon);
            if (!grid.TryGetValue(key, out var bucket))
            {
                bucket = [];
                grid[key] = bucket;
            }

            bucket.Add((lat, lon));
        }

        return grid;
    }

    private static (int, int) CellKey(double lat, double lon) =>
        ((int)Math.Floor(lat / CellDegrees), (int)Math.Floor(lon / CellDegrees));

    private static bool NearClaim(
        Dictionary<(int, int), List<(double Lat, double Lon)>> grid,
        double lat,
        double lon)
    {
        var (cx, cy) = CellKey(lat, lon);
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                if (!grid.TryGetValue((cx + dx, cy + dy), out var bucket))
                    continue;

                foreach (var (clat, clon) in bucket)
                {
                    if (ApproxKm(lat, lon, clat, clon) <= ClaimProximityKm)
                        return true;
                }
            }
        }

        return false;
    }

    private static double ApproxKm(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = (lat1 - lat2) * 111.0;
        var dLon = (lon1 - lon2) * 111.0 * Math.Cos(lat1 * Math.PI / 180.0);
        return Math.Sqrt(dLat * dLat + dLon * dLon);
    }
}

public readonly record struct MineOwnershipEnrichmentResult(
    int Scanned,
    int Updated,
    int HintPrivate,
    int HintPublic,
    int ClaimTagged,
    int Skipped);
