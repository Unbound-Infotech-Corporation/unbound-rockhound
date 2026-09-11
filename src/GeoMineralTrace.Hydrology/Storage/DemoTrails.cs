using GeoMineralTrace.Core.Geo;
using GeoMineralTrace.Core.Trails;
using GeoMineralTrace.Hydrology.Geo;

namespace GeoMineralTrace.Hydrology.Storage;

/// <summary>Bundled seed trails near popular rockhounding corridors (offline).</summary>
internal static class DemoTrails
{
    public static IEnumerable<Trail> Create()
    {
        yield return Trail(
            "seed:crystal-park-co",
            "Crystal Park Access Trail",
            "CO",
            "Teller",
            TrailKind.Hiking,
            "Curated approach trail near Crystal Park fee dig area.",
            [
                (38.8120, -105.0160),
                (38.8145, -105.0125),
                (38.8170, -105.0080)
            ]);

        yield return Trail(
            "seed:topaz-mountain-ut",
            "Topaz Mountain Access Road",
            "UT",
            "Juab",
            TrailKind.AccessRoad,
            "BLM access toward Topaz Mountain collecting area.",
            [
                (39.7100, -113.1000),
                (39.7200, -113.0900),
                (39.7300, -113.0850)
            ]);

        yield return Trail(
            "seed:opal-butte-or",
            "Opal Butte Approach",
            "OR",
            "Morrow",
            TrailKind.Pack,
            "Approach route toward Opal Butte area (verify land status).",
            [
                (45.1800, -119.6200),
                (45.1850, -119.6100),
                (45.1900, -119.6000)
            ]);

        yield return Trail(
            "seed:herkimer-ny",
            "Herkimer Diamond Mines Loop",
            "NY",
            "Herkimer",
            TrailKind.Hiking,
            "Trail near commercial Herkimer dig sites.",
            [
                (43.0200, -74.9800),
                (43.0225, -74.9750),
                (43.0250, -74.9700)
            ]);

        yield return Trail(
            "seed:jade-cove-ca",
            "Jade Cove Coastal Path",
            "CA",
            "Monterey",
            TrailKind.Hiking,
            "Coastal path near Jade Cove / Big Sur jade hunting shoreline.",
            [
                (35.9200, -121.4700),
                (35.9220, -121.4680),
                (35.9240, -121.4660)
            ]);

        yield return Trail(
            "seed:thunder-egg-or",
            "Richardson Ranch Trail",
            "OR",
            "Jefferson",
            TrailKind.Hiking,
            "Access near Central Oregon thunderegg country.",
            [
                (44.5000, -121.1500),
                (44.5050, -121.1400),
                (44.5100, -121.1300)
            ]);

        yield return Trail(
            "seed:montana-sapphire",
            "Gem Mountain Access",
            "MT",
            "Granite",
            TrailKind.AccessRoad,
            "Approach toward Philipsburg / Gem Mountain sapphire digs.",
            [
                (46.3200, -113.3000),
                (46.3250, -113.2900),
                (46.3300, -113.2800)
            ]);

        yield return Trail(
            "seed:arkansas-quartz",
            "Crystal Mountain Trail",
            "AR",
            "Montgomery",
            TrailKind.Hiking,
            "Trail corridor near Ouachita quartz dig areas.",
            [
                (34.5200, -93.6200),
                (34.5250, -93.6100),
                (34.5300, -93.6000)
            ]);
    }

    private static Trail Trail(
        string externalId,
        string name,
        string state,
        string county,
        TrailKind kind,
        string notes,
        (double Lat, double Lon)[] verts)
    {
        var vertices = verts.Select(v => new GeoCoordinate(v.Lat, v.Lon)).ToList();
        var centroid = new GeoCoordinate(
            vertices.Average(v => v.LatitudeDegrees),
            vertices.Average(v => v.LongitudeDegrees));

        return new Trail
        {
            Id = Guid.NewGuid(),
            ExternalId = externalId,
            Name = name,
            StateCode = state,
            County = county,
            Kind = kind,
            LengthKm = ApproxLengthKm(vertices),
            Vertices = vertices,
            Centroid = centroid,
            Sources = ["seed-demo"],
            SourceDataset = "UnboundRockhound demo trails",
            Notes = notes
        };
    }

    private static double ApproxLengthKm(IReadOnlyList<GeoCoordinate> vertices)
    {
        double sum = 0;
        for (var i = 1; i < vertices.Count; i++)
            sum += PolylineDistance.HaversineKm(vertices[i - 1], vertices[i]);
        return sum;
    }
}
