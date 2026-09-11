namespace GeoMineralTrace.Core.Hydrology;

/// <summary>Mineral or gem linked to a watercourse — curated or proximity-inferred.</summary>
public sealed class RiverMineralAssociation
{
    public required Guid WatercourseId { get; init; }
    public required string MineralName { get; init; }
    public MineralAssociationKind AssociationKind { get; init; }
    public AssociationConfidence Confidence { get; init; } = AssociationConfidence.Medium;
    public Guid? SourceLocalityId { get; init; }
    public double? DistanceKm { get; init; }
    public string? Citation { get; init; }
    public string? Notes { get; init; }
}

public enum MineralAssociationKind
{
    Curated = 0,
    ProximityInferred = 1
}

public enum AssociationConfidence
{
    Low = 0,
    Medium = 1,
    High = 2
}
