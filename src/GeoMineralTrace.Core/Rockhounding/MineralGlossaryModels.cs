namespace GeoMineralTrace.Core.Rockhounding;

/// <summary>Offline mineral/gem glossary species row (glossary.db).</summary>
public sealed class MineralSpecies
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public IReadOnlyList<string> Aliases { get; init; } = [];
    public string? Formula { get; init; }
    public string? CrystalSystem { get; init; }
    public double? MohsMin { get; init; }
    public double? MohsMax { get; init; }
    public string? ColorRange { get; init; }
    public string? Luster { get; init; }
    /// <summary>IMA approval status, gem variety, or rock/informal label.</summary>
    public string? ImaStatus { get; init; }
    public int? ImaYear { get; init; }
    public required string Description { get; init; }
    public string? WikidataId { get; init; }
    public DateTimeOffset UpdatedUtc { get; init; }
    public int ImageCount { get; init; }
}

/// <summary>Licensed image metadata; file may be absent (text-only species).</summary>
public sealed class MineralImage
{
    public required string Id { get; init; }
    public required string SpeciesId { get; init; }
    /// <summary>Absolute or AppData-relative path; null if not downloaded.</summary>
    public string? FilePath { get; init; }
    public required string SourceUrl { get; init; }
    /// <summary>CC0, CC-BY, CC-BY-SA, or Public Domain.</summary>
    public required string LicenseType { get; init; }
    public required string AttributionText { get; init; }
    public string? Photographer { get; init; }
    public int SortOrder { get; init; }
}

public sealed class MineralSpeciesFilter
{
    public string? NameQuery { get; init; }
    public string? CrystalSystem { get; init; }
    public double? MohsMin { get; init; }
    public double? MohsMax { get; init; }
    public string? ColorContains { get; init; }
    public string? StateCode { get; init; }
}
