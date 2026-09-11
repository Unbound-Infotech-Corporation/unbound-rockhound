using FluentAssertions;
using GeoMineralTrace.Core.Rockhounding;
using GeoMineralTrace.Rockhounding.Import;
using GeoMineralTrace.Rockhounding.Scoring;
using GeoMineralTrace.Rockhounding.Storage;

namespace GeoMineralTrace.Rockhounding.Tests;

public class UsgsMrdsImporterTests
{
    private readonly UsgsMrdsImporter _importer = new();

    [Fact]
    public void Parse_sample_rows_maps_occurrences()
    {
        var csv = """
            dep_id,site_name,state,county,latitude,longitude,commod1,dep_type,oper_status
            OR000001,Demo Obsidian Flow,OR,Lake,43.560,-120.070,obsidian,Occurrence,
            """;

        var localities = _importer.Parse(csv);
        localities.Should().HaveCount(1);
        localities[0].Name.Should().Contain("Obsidian");
        localities[0].ExternalId.Should().Be("usgs-mrds:OR000001");
        localities[0].SourceDataset.Should().Be(UsgsMrdsImporter.MrdsSourceName);
        localities[0].SourceVintage.Should().Be(UsgsMrdsImporter.MrdsVintage);
        localities[0].HumanActivityDataMayBeOutdated.Should().BeTrue();
        localities[0].SystemRating!.LegalClarity.Should().Be(LocalityRatingCalculator.LegalClarityForUsgsOccurrence);
        localities[0].Sources.Should().Contain(UsgsMrdsImporter.MrdsSourceName);
        localities[0].HazardNotes.Should().Contain("outdated");
    }

    [Fact]
    public void Past_producer_maps_to_restricted_with_low_legal_clarity()
    {
        var csv = """
            dep_id,site_name,state,latitude,longitude,commod1,dep_type,oper_status
            X1,Test Mine,NV,38.0,-117.0,gold,Vein,Past Producer
            """;

        var loc = _importer.Parse(csv).Single();
        loc.AccessStatus.Should().Be(AccessStatus.Restricted);
        loc.CollectingLimits.Should().Contain("MRDS");
        loc.SystemRating!.LegalClarity.Should().Be(2.0);
        loc.SystemRating.LegalClarity.Should().BeLessThan(
            LocalityRatingCalculator.LegalClarityFromAccess(AccessStatus.Open));
    }

    [Fact]
    public void Preview_reports_counts()
    {
        var csv = """
            dep_id,site_name,state,latitude,longitude,commod1
            A1,Site A,OR,44.0,-120.0,quartz
            B2,NoCoords,OR,,,quartz
            """;

        var preview = _importer.Preview(csv);
        preview.TotalRows.Should().Be(2);
        preview.ImportableRows.Should().Be(1);
        preview.SkippedNoCoords.Should().Be(1);
    }

    [Fact]
    public void National_header_maps_dev_stat_and_geology()
    {
        var csv = """
            dep_id,site_name,latitude,longitude,country,state,county,commod1,dep_type,dev_stat,ore,hrock_type,ref
            10009306,Horse Mountain,39.69999,-106.20061,United States,Colorado,Eagle,Uranium,Occurrence,Occurrence,uraninite,sandstone,Smith 1990
            """;

        var loc = _importer.Parse(csv).Single();
        loc.StateCode.Should().Be("CO");
        loc.ReportedMinerals.Should().Contain("uranium");
        loc.EnrichmentSummary.Should().Contain("Host rock");
        loc.EnrichmentSummary.Should().Contain("Ore");
        loc.Sources.Should().Contain(s => s.Contains("Smith", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Critical_minerals_csv_maps_as_tagged_supplement()
    {
        var csv = """
            Deposit,Lat_WGS84,Long_WGS84,State,MinSystem,DepType,CritMin,FocusArea,DepCat,Production,Resources,Source_s,Links
            21 and 27 fluorspar mines,34.97589,-107.98506,NM,Magmatic REE,Fluorspar,fluorspar,Zuni Mountains,CM-past producer,, ,MRDS,https://example.test
            """;

        var loc = _importer.Parse(csv).Single();
        loc.SourceDataset.Should().Be(UsgsMrdsImporter.CritMinSourceName);
        loc.SourceVintage.Should().Be(UsgsMrdsImporter.CritMinVintage);
        loc.ExternalId.Should().StartWith("usgs-critmin:");
        loc.ReportedMinerals.Should().Contain("fluorspar");
        loc.SystemRating!.LegalClarity.Should().Be(LocalityRatingCalculator.LegalClarityForUsgsOccurrence);
        loc.HumanActivityDataMayBeOutdated.Should().BeTrue();
    }

    [Fact]
    public async Task Import_dedupes_seed_by_name_state_coords_and_logs_invalid()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gmt-mrds-{Guid.NewGuid():N}.db");
        try
        {
            await using var store = new LocalityStore(path);
            await store.InitializeAsync();

            var seed = new Locality
            {
                Id = Guid.NewGuid(),
                Name = "Shared Site",
                StateCode = "OR",
                Coordinates = new GeoMineralTrace.Core.Geo.GeoCoordinate(44.0, -120.0),
                ReportedMinerals = ["quartz"],
                Sources = ["seed"],
                AccessStatus = AccessStatus.Open,
                SystemRating = LocalityRatingCalculator.Create(8, 8, 9, 8, 8, 5)
            };
            await store.UpsertAsync(seed);

            var csvPath = Path.Combine(Path.GetTempPath(), $"gmt-mrds-{Guid.NewGuid():N}.csv");
            await File.WriteAllTextAsync(csvPath, """
                dep_id,site_name,state,latitude,longitude,country,commod1
                D1,Shared Site,OR,44.0001,-120.0001,United States,quartz
                D2,Bad Coords,OR,,,United States,gold
                D3,Foreign,BC,50.0,-120.0,Canada,copper
                """);

            var result = await _importer.ImportFileWithStatsAsync(csvPath, store, unitedStatesOnly: true);
            result.Upserted.Should().Be(1);
            result.DedupedAgainstExisting.Should().Be(1);
            result.InvalidCoordinates.Should().Be(1);
            result.SkippedNonUs.Should().Be(1);
            result.CountsByState["OR"].Should().Be(1);

            var merged = await store.GetByExternalIdAsync("usgs-mrds:D1");
            merged.Should().NotBeNull();
            merged!.Id.Should().Be(seed.Id);
            merged.Sources.Should().Contain("seed");
            merged.HumanActivityDataMayBeOutdated.Should().BeTrue();
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }
}
