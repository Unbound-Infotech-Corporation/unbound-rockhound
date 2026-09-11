using GeoMineralTrace.Core.Geo;

namespace GeoMineralTrace_App.Helpers;

/// <summary>Offline U.S. state / territory geographic centers for quick home-location picks (no network).</summary>
public static class UsPlaceCentroids
{
    public sealed record Place(string Code, string Name, double Latitude, double Longitude)
    {
        public GeoCoordinate Coordinate => new(Latitude, Longitude);
        public string Display => $"{Name} ({Code})";
    }

    public static IReadOnlyList<Place> All { get; } =
    [
        new("AL", "Alabama", 32.806671, -86.791130),
        new("AK", "Alaska", 61.370716, -152.404419),
        new("AZ", "Arizona", 33.729759, -111.431221),
        new("AR", "Arkansas", 34.969704, -92.373123),
        new("CA", "California", 36.116203, -119.681564),
        new("CO", "Colorado", 39.059811, -105.311104),
        new("CT", "Connecticut", 41.597782, -72.755371),
        new("DE", "Delaware", 39.318523, -75.507141),
        new("FL", "Florida", 27.766279, -81.686783),
        new("GA", "Georgia", 33.040619, -83.643074),
        new("HI", "Hawaii", 21.094318, -157.498337),
        new("ID", "Idaho", 44.240459, -114.478828),
        new("IL", "Illinois", 40.349457, -88.986137),
        new("IN", "Indiana", 39.849426, -86.258278),
        new("IA", "Iowa", 42.011539, -93.210526),
        new("KS", "Kansas", 38.526600, -96.726486),
        new("KY", "Kentucky", 37.668140, -84.670067),
        new("LA", "Louisiana", 31.169546, -91.867805),
        new("ME", "Maine", 44.693947, -69.381927),
        new("MD", "Maryland", 39.063946, -76.802101),
        new("MA", "Massachusetts", 42.230171, -71.530106),
        new("MI", "Michigan", 43.326618, -84.536095),
        new("MN", "Minnesota", 45.694454, -93.900192),
        new("MS", "Mississippi", 32.741646, -89.678696),
        new("MO", "Missouri", 38.456085, -92.288368),
        new("MT", "Montana", 46.921925, -110.454353),
        new("NE", "Nebraska", 41.125370, -98.268082),
        new("NV", "Nevada", 38.313515, -117.055374),
        new("NH", "New Hampshire", 43.452492, -71.563896),
        new("NJ", "New Jersey", 40.298904, -74.521011),
        new("NM", "New Mexico", 34.840515, -106.248482),
        new("NY", "New York", 42.165726, -74.948051),
        new("NC", "North Carolina", 35.630066, -79.806419),
        new("ND", "North Dakota", 47.528912, -99.784012),
        new("OH", "Ohio", 40.388783, -82.764915),
        new("OK", "Oklahoma", 35.565342, -96.928917),
        new("OR", "Oregon", 44.572021, -122.070938),
        new("PA", "Pennsylvania", 40.590752, -77.209755),
        new("RI", "Rhode Island", 41.680893, -71.511780),
        new("SC", "South Carolina", 33.856892, -80.945007),
        new("SD", "South Dakota", 44.299782, -99.438828),
        new("TN", "Tennessee", 35.747845, -86.692345),
        new("TX", "Texas", 31.054487, -97.563461),
        new("UT", "Utah", 40.150032, -111.862434),
        new("VT", "Vermont", 44.045876, -72.710686),
        new("VA", "Virginia", 37.769337, -78.169968),
        new("WA", "Washington", 47.400902, -121.490494),
        new("WV", "West Virginia", 38.491226, -80.954453),
        new("WI", "Wisconsin", 44.268543, -89.616508),
        new("WY", "Wyoming", 42.755966, -107.302490),
    ];
}
