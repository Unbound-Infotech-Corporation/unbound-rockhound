using GeoMineralTrace.Core.Geo;
using GeoMineralTrace.Core.Hydrology;

namespace GeoMineralTrace.Hydrology.Storage;

/// <summary>Demo named rivers for dev / first-run seed.</summary>
public static class DemoWatercourses
{
    public static IReadOnlyList<Watercourse> Create() =>
    [
        new()
        {
            Id = Guid.Parse("a1000001-0000-4000-8000-000000000001"),
            ExternalId = "seed:arkansas-river-co",
            Name = "Arkansas River",
            StateCode = "CO",
            County = "Chaffee",
            Kind = WatercourseKind.River,
            SourceDataset = "UnboundRockhound seed",
            SourceVintage = "2026",
            Sources = ["https://www.mindat.org/"],
            Centroid = new GeoCoordinate(38.85, -106.12),
            LengthKm = 12.0,
            Vertices =
            [
                new GeoCoordinate(38.90, -106.18),
                new GeoCoordinate(38.87, -106.14),
                new GeoCoordinate(38.85, -106.12),
                new GeoCoordinate(38.80, -106.08)
            ]
        },
        new()
        {
            Id = Guid.Parse("a1000002-0000-4000-8000-000000000002"),
            ExternalId = "seed:rogue-river-or",
            Name = "Rogue River",
            StateCode = "OR",
            County = "Jackson",
            Kind = WatercourseKind.River,
            SourceDataset = "UnboundRockhound seed",
            SourceVintage = "2026",
            Sources = ["Oregon rockhounding field guides"],
            Centroid = new GeoCoordinate(42.45, -123.35),
            LengthKm = 18.0,
            Vertices =
            [
                new GeoCoordinate(42.50, -123.40),
                new GeoCoordinate(42.47, -123.37),
                new GeoCoordinate(42.45, -123.35),
                new GeoCoordinate(42.40, -123.30)
            ]
        },
        new()
        {
            Id = Guid.Parse("a1000003-0000-4000-8000-000000000003"),
            ExternalId = "seed:missouri-river-mt",
            Name = "Missouri River",
            StateCode = "MT",
            County = "Cascade",
            Kind = WatercourseKind.River,
            SourceDataset = "UnboundRockhound seed",
            SourceVintage = "2026",
            Sources = ["Montana sapphire gravel bars — public literature"],
            Centroid = new GeoCoordinate(47.50, -111.30),
            LengthKm = 25.0,
            Vertices =
            [
                new GeoCoordinate(47.55, -111.35),
                new GeoCoordinate(47.52, -111.32),
                new GeoCoordinate(47.50, -111.30),
                new GeoCoordinate(47.45, -111.25)
            ]
        }
    ];
}
