using System.Text.RegularExpressions;

namespace GeoMineralTrace.Core.Social;

/// <summary>
/// Heuristic US-state → centroid resolver for community Reddit text.
/// Used by desktop "Process community NER" preview and documented for the edge function.
/// Does not write to claims/rumoured stores.
/// </summary>
public static class CommunityPlaceHeuristic
{
    public sealed record StateHit(string Code, string Name, double Latitude, double Longitude);

    private static readonly (string Code, string Name, double Lat, double Lon)[] States =
    [
        ("AL", "Alabama", 32.806671, -86.791130),
        ("AK", "Alaska", 61.370716, -152.404419),
        ("AZ", "Arizona", 33.729759, -111.431221),
        ("AR", "Arkansas", 34.969704, -92.373123),
        ("CA", "California", 36.116203, -119.681564),
        ("CO", "Colorado", 39.059811, -105.311104),
        ("CT", "Connecticut", 41.597782, -72.755371),
        ("DE", "Delaware", 39.318523, -75.507141),
        ("FL", "Florida", 27.766279, -81.686783),
        ("GA", "Georgia", 33.040619, -83.643074),
        ("HI", "Hawaii", 21.094318, -157.498337),
        ("ID", "Idaho", 44.240459, -114.478828),
        ("IL", "Illinois", 40.349457, -88.986137),
        ("IN", "Indiana", 39.849426, -86.258278),
        ("IA", "Iowa", 42.011539, -93.210526),
        ("KS", "Kansas", 38.526600, -96.726486),
        ("KY", "Kentucky", 37.668140, -84.670067),
        ("LA", "Louisiana", 31.169546, -91.867805),
        ("ME", "Maine", 44.693947, -69.381927),
        ("MD", "Maryland", 39.063946, -76.802101),
        ("MA", "Massachusetts", 42.230171, -71.530106),
        ("MI", "Michigan", 43.326618, -84.536095),
        ("MN", "Minnesota", 45.694454, -93.900192),
        ("MS", "Mississippi", 32.741646, -89.678696),
        ("MO", "Missouri", 38.456085, -92.288368),
        ("MT", "Montana", 46.921925, -110.454353),
        ("NE", "Nebraska", 41.125370, -98.268082),
        ("NV", "Nevada", 38.313515, -117.055374),
        ("NH", "New Hampshire", 43.452492, -71.563896),
        ("NJ", "New Jersey", 40.298904, -74.521011),
        ("NM", "New Mexico", 34.840515, -106.248482),
        ("NY", "New York", 42.165726, -74.948051),
        ("NC", "North Carolina", 35.630066, -79.806419),
        ("ND", "North Dakota", 47.528912, -99.784012),
        ("OH", "Ohio", 40.388783, -82.764915),
        ("OK", "Oklahoma", 35.565342, -96.928917),
        ("OR", "Oregon", 44.572021, -122.070938),
        ("PA", "Pennsylvania", 40.590752, -77.209755),
        ("RI", "Rhode Island", 41.680893, -71.511780),
        ("SC", "South Carolina", 33.856892, -80.945007),
        ("SD", "South Dakota", 44.299782, -99.438828),
        ("TN", "Tennessee", 35.747845, -86.692345),
        ("TX", "Texas", 31.054487, -97.563461),
        ("UT", "Utah", 40.150032, -111.862434),
        ("VT", "Vermont", 44.045876, -72.710686),
        ("VA", "Virginia", 37.769337, -78.169968),
        ("WA", "Washington", 47.400902, -121.490494),
        ("WV", "West Virginia", 38.491226, -80.954453),
        ("WI", "Wisconsin", 44.268543, -89.616508),
        ("WY", "Wyoming", 42.755966, -107.302490),
    ];

    /// <summary>First matching US state name or postal code in <paramref name="text"/>.</summary>
    public static StateHit? TryResolveUsState(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        foreach (var (code, name, lat, lon) in States)
        {
            if (Regex.IsMatch(text, $@"\b{Regex.Escape(name)}\b", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(text, $@"\b{Regex.Escape(code)}\b"))
            {
                return new StateHit(code, name, lat, lon);
            }
        }

        return null;
    }
}
