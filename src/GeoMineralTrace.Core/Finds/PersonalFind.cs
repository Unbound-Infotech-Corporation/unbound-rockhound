using GeoMineralTrace.Core.Geo;

namespace GeoMineralTrace.Core.Finds;

/// <summary>
/// User-logged personal find / field note (offline). Distinct from catalog TypicalFinds.
/// </summary>
public sealed class PersonalFind
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public GeoCoordinate? Coordinates { get; init; }
    public string? StateCode { get; init; }
    public IReadOnlyList<string> Minerals { get; init; } = [];
    public string? Notes { get; init; }
    public DateOnly? FoundOn { get; init; }
    public DateTimeOffset? CreatedUtc { get; init; }
    public string? LinkedLocalityName { get; init; }
}
