using FluentAssertions;
using GeoMineralTrace.Core.Geo;
using GeoMineralTrace.Rockhounding.Storage;
using Microsoft.Data.Sqlite;

namespace GeoMineralTrace.Rockhounding.Tests;

public class LocalityStoreTests
{
    [Fact]
    public async Task SeedAndQuery_WorksOffline()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gmt-test-{Guid.NewGuid():N}.db");
        try
        {
            await using var store = new LocalityStore(path);
            await store.InitializeAsync();
            await store.SeedDemoDataAsync();

            var or = await store.ListByStateAsync("OR");
            or.Should().Contain(l => l.Name.Contains("Obsidian", StringComparison.OrdinalIgnoreCase));

            var garnet = await store.SearchByMineralAsync("garnet");
            garnet.Should().NotBeEmpty();

            var near = await store.FindNearAsync(new GeoCoordinate(44.08, -120.43), radiusKm: 50);
            near.Should().NotBeEmpty();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path))
            {
                try { File.Delete(path); }
                catch (IOException) { /* best-effort cleanup on Windows file locks */ }
            }
        }
    }

    [Fact]
    public void Haversine_KnownDistance_IsReasonable()
    {
        // Roughly NYC to Philadelphia ~130 km
        var nyc = new GeoCoordinate(40.7128, -74.0060);
        var phl = new GeoCoordinate(39.9526, -75.1652);
        var km = LocalityStore.HaversineKm(nyc, phl);
        km.Should().BeInRange(110, 160);
    }
}
