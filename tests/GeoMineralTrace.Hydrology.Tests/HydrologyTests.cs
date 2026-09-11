using FluentAssertions;
using GeoMineralTrace.Core.Geo;
using GeoMineralTrace.Core.Hydrology;
using GeoMineralTrace.Hydrology.Enrichment;
using GeoMineralTrace.Hydrology.Geo;
using GeoMineralTrace.Hydrology.Import;
using GeoMineralTrace.Hydrology.Storage;
using GeoMineralTrace.Rockhounding.Storage;

namespace GeoMineralTrace.Hydrology.Tests;

public sealed class PolylineDistanceTests
{
    [Fact]
    public void MinDistanceKm_point_on_segment_is_near_zero()
    {
        var point = new GeoCoordinate(39.0, -105.0);
        var line = new[]
        {
            new GeoCoordinate(38.9, -105.1),
            new GeoCoordinate(39.0, -105.0),
            new GeoCoordinate(39.1, -104.9)
        };

        PolylineDistance.MinDistanceKm(point, line).Should().BeLessThan(0.05);
    }
}

public sealed class NhdWatercourseImporterTests
{
    [Fact]
    public async Task ImportFile_parses_named_seed_geojson()
    {
        var path = FindSeedGeoJson();
        var db = Path.Combine(Path.GetTempPath(), $"rivers-test-{Guid.NewGuid():N}.db");
        await using var store = new RiverStore(db);
        await store.InitializeAsync();

        var importer = new NhdWatercourseImporter();
        var result = await importer.ImportFileAsync(path, store);

        result.Imported.Should().BeGreaterThan(0);
        (await store.CountAsync()).Should().BeGreaterThan(0);

        var arkansas = await store.GetByExternalIdAsync("seed:arkansas-river-co");
        arkansas.Should().NotBeNull();
        arkansas!.Name.Should().Contain("Arkansas");
    }

    private static string FindSeedGeoJson()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir.FullName, "data", "seed", "rivers-sample.geojson");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        var repo = Path.Combine(
            Directory.GetCurrentDirectory(),
            "..", "..", "..", "..", "data", "seed", "rivers-sample.geojson");
        return Path.GetFullPath(repo);
    }
}

public sealed class RiverMineralEnricherTests
{
    [Fact]
    public async Task Enrich_skips_curated_duplicate_minerals()
    {
        var db = Path.Combine(Path.GetTempPath(), $"rivers-enrich-{Guid.NewGuid():N}.db");
        var locDb = Path.Combine(Path.GetTempPath(), $"loc-enrich-{Guid.NewGuid():N}.db");

        await using var rivers = new RiverStore(db);
        await using var localities = new LocalityStore(locDb);
        await rivers.InitializeAsync();
        await localities.InitializeAsync();

        var riverId = Guid.NewGuid();
        await rivers.UpsertAsync(new Watercourse
        {
            Id = riverId,
            ExternalId = "test:river",
            Name = "Test River",
            StateCode = "CO",
            Centroid = new GeoCoordinate(38.85, -106.12),
            Vertices =
            [
                new GeoCoordinate(38.86, -106.13),
                new GeoCoordinate(38.85, -106.12),
                new GeoCoordinate(38.84, -106.11)
            ],
            LengthKm = 2.0
        });

        await rivers.UpsertMineralAsync(new RiverMineralAssociation
        {
            WatercourseId = riverId,
            MineralName = "quartz",
            AssociationKind = MineralAssociationKind.Curated,
            Confidence = AssociationConfidence.High,
            Citation = "field guide"
        });

        await localities.UpsertAsync(new Core.Rockhounding.Locality
        {
            Id = Guid.NewGuid(),
            Name = "Near Test",
            StateCode = "CO",
            Coordinates = new GeoCoordinate(38.8501, -106.1201),
            ReportedMinerals = ["quartz", "feldspar"]
        });

        var enricher = new RiverMineralEnricher();
        var result = await enricher.EnrichFromLocalitiesAsync(rivers, localities, corridorRadiusKm: 3);

        result.AssociationsAdded.Should().Be(1);
        result.SkippedCuratedDup.Should().BeGreaterThan(0);

        var minerals = await rivers.GetMineralsForWatercourseAsync(riverId);
        minerals.Count(m => m.MineralName == "quartz").Should().Be(1);
        minerals.Should().Contain(m => m.MineralName == "feldspar");
    }
}

public sealed class RiverStoreUpsertTests
{
    [Fact]
    public async Task Upsert_by_external_id_is_idempotent()
    {
        var db = Path.Combine(Path.GetTempPath(), $"rivers-upsert-{Guid.NewGuid():N}.db");
        await using var store = new RiverStore(db);
        await store.InitializeAsync();

        var wc = DemoWatercourses.Create()[0];
        await store.UpsertAsync(wc);
        await store.UpsertAsync(wc);

        (await store.CountAsync()).Should().BeGreaterThanOrEqualTo(1);
    }
}
