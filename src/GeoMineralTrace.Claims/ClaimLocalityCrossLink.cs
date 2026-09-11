using GeoMineralTrace.Core.Claims;
using GeoMineralTrace.Core.Geo;
using GeoMineralTrace.Core.Rockhounding;
using GeoMineralTrace.Claims.Storage;
using GeoMineralTrace.Rockhounding.Storage;

namespace GeoMineralTrace.Claims;

/// <summary>
/// Offline proximity cross-link between mining claims and rockhounding localities.
/// </summary>
public sealed class ClaimLocalityCrossLink
{
    private readonly ClaimStore _claims;
    private readonly LocalityStore _localities;

    public ClaimLocalityCrossLink(ClaimStore claims, LocalityStore localities)
    {
        _claims = claims;
        _localities = localities;
    }

    public sealed record NearbyClaimHit(MiningClaim Claim, double DistanceKm);
    public sealed record NearbyLocalityHit(Locality Locality, double DistanceKm);

    public async Task<IReadOnlyList<NearbyClaimHit>> ClaimsNearLocalityAsync(
        Locality locality,
        double radiusKm = 5,
        CancellationToken cancellationToken = default)
    {
        if (locality.Coordinates is not { } center)
            return [];

        var claims = await _claims.FindNearAsync(center, radiusKm, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return claims
            .Where(c => c.Coordinates is not null)
            .Select(c => new NearbyClaimHit(c, ClaimStore.HaversineKm(center, c.Coordinates!.Value)))
            .OrderBy(h => h.DistanceKm)
            .ToList();
    }

    public async Task<IReadOnlyList<NearbyLocalityHit>> LocalitiesNearClaimAsync(
        MiningClaim claim,
        double radiusKm = 5,
        CancellationToken cancellationToken = default)
    {
        if (claim.Coordinates is not { } center)
            return [];

        var localities = await _localities.FindNearAsync(center, radiusKm, cancellationToken)
            .ConfigureAwait(false);
        return localities
            .Where(l => l.Coordinates is not null)
            .Select(l => new NearbyLocalityHit(l, ClaimStore.HaversineKm(center, l.Coordinates!.Value)))
            .OrderBy(h => h.DistanceKm)
            .ToList();
    }
}
