using FluentAssertions;
using GeoMineralTrace.Claims;
using GeoMineralTrace.Claims.Import;
using GeoMineralTrace.Claims.Storage;
using GeoMineralTrace.Core.Claims;

namespace GeoMineralTrace.Claims.Tests;

public class BlmMlrsClaimImporterTests
{
    private readonly BlmMlrsClaimImporter _importer = new();

    [Fact]
    public void GeoJson_maps_active_lode_centroid_and_plss()
    {
        var geojson = """
            {"type":"FeatureCollection","features":[{
              "type":"Feature",
              "geometry":{"type":"Polygon","coordinates":[[[-117.0,39.84],[-117.01,39.84],[-117.01,39.85],[-117.0,39.85],[-117.0,39.84]]]},
              "properties":{
                "CSE_NAME":"MAGA #1","CSE_NR":"NV105221812","BLM_PROD":"Lode Claim","CSE_DISP":"Active",
                "CSE_META":"NV 21 0230N 0440E 022 A SWSE","RCRD_ACRS":20.66,"Created":1611705600000
              }}]}
            """;

        var claims = _importer.ParseGeoJson(geojson, new DateOnly(2026, 8, 27), DateTimeOffset.UtcNow);
        claims.Should().HaveCount(1);
        var c = claims[0];
        c.SerialNumber.Should().Be("NV105221812");
        c.ClaimType.Should().Be(ClaimType.Lode);
        c.Status.Should().Be(ClaimStatus.Active);
        c.StateCode.Should().Be("NV");
        c.Township.Should().Be("0230N");
        c.Coordinates.Should().NotBeNull();
        c.LegalNotes.Should().Contain("not sold");
        c.LegalNotes.Should().ContainEquivalentOf("private");
        c.MaintenanceDeadlineApproaching.Should().BeTrue(); // Aug 27 within Sept 1 window, no fee evidence
    }

    [Fact]
    public void Csv_with_stale_assessment_marks_expiring_soon()
    {
        var csv = """
            serial_number,claim_name,claim_type,status,state,latitude,longitude,last_assessment_year,last_maintenance_fee_paid
            NV1,Soon Placer,Placer Claim,Active,NV,39.8,-117.0,2024,2024-08-15
            """;

        var c = _importer.ParseCsv(csv, new DateOnly(2026, 8, 27), DateTimeOffset.UtcNow).Single();
        c.Status.Should().Be(ClaimStatus.ExpiringSoon);
        c.LegalNotes.Should().Contain("EXPIRING SOON");
        c.LegalNotes.Should().NotContain("buy from BLM");
        c.LegalNotes.ToLowerInvariant().Should().NotContain("available to buy");
    }

    [Fact]
    public void Closed_disposition_maps_to_lapsed_reopenable()
    {
        var csv = """
            serial_number,claim_name,claim_type,status,state,latitude,longitude
            NV2,Old Claim,Lode Claim,Closed,NV,38.2,-117.5
            """;
        var c = _importer.ParseCsv(csv, new DateOnly(2026, 8, 27), DateTimeOffset.UtcNow).Single();
        c.Status.Should().Be(ClaimStatus.LapsedReopenable);
        c.LegalNotes.Should().Contain("staked");
    }

    [Fact]
    public async Task Import_upserts_into_store()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gmt-claims-{Guid.NewGuid():N}.db");
        var csvPath = Path.Combine(Path.GetTempPath(), $"gmt-claims-{Guid.NewGuid():N}.csv");
        try
        {
            await File.WriteAllTextAsync(csvPath, """
                serial_number,claim_name,claim_type,status,state,latitude,longitude,last_assessment_year
                NV105221812,MAGA #1,Lode Claim,Active,NV,39.84,-117.01,2026
                NV105757234,BIGE-180,Lode Claim,Closed,NV,38.26,-117.51,
                """);
            await using var store = new ClaimStore(path);
            await store.InitializeAsync();
            var result = await _importer.ImportFileAsync(csvPath, store, asOf: new DateOnly(2026, 8, 27));
            result.Upserted.Should().Be(2);
            result.CountsByState["NV"].Should().Be(2);
            (await store.GetByExternalIdAsync("blm-mlrs:NV105221812")).Should().NotBeNull();
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
            try { File.Delete(csvPath); } catch { /* ignore */ }
        }
    }
}

public class ClaimStatusClassifierTests
{
    [Fact]
    public void Next_september_first_from_august_is_same_year()
    {
        ClaimStatusClassifier.NextSeptemberFirst(new DateOnly(2026, 8, 27))
            .Should().Be(new DateOnly(2026, 9, 1));
    }

    [Fact]
    public void Explainers_never_say_available_to_buy()
    {
        foreach (ClaimStatus s in Enum.GetValues<ClaimStatus>())
        {
            var text = ClaimStatusClassifier.LegalExplainer(s).ToLowerInvariant();
            text.Should().NotContain("available to buy");
            text.Should().NotContain("buy from blm");
            text.Should().NotContain("claims for sale");
        }
    }
}
