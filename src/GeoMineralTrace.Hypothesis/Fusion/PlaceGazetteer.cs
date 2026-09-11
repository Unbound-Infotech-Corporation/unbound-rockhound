using System.Globalization;
using GeoMineralTrace.Core.Geo;

namespace GeoMineralTrace.Hypothesis.Fusion;

/// <summary>
/// Offline place→coordinate table for U.S. rockhounding / SW desert / landmark leads.
/// Not a full gazetteer — enough to plot place-mention hypotheses on the map.
/// </summary>
public static class PlaceGazetteer
{
    private static readonly Dictionary<string, GeoCoordinate> Places = new(StringComparer.OrdinalIgnoreCase)
    {
        // Rockhounding classics
        ["maury mountain"] = new(44.076, -120.758),
        ["glass buttes"] = new(43.545, -120.018),
        ["richardson ranch"] = new(44.48, -121.15),
        ["succor creek"] = new(43.47, -117.14),
        ["opal creek"] = new(44.87, -122.25),
        ["thunder egg"] = new(44.35, -120.85),
        ["prineville"] = new(44.30, -120.85),
        ["hampton butte"] = new(43.52, -120.25),
        ["white fir springs"] = new(44.35, -120.70),
        ["ochoco"] = new(44.40, -120.40),
        ["steens mountain"] = new(42.64, -118.58),
        ["leslie gulch"] = new(43.32, -117.23),
        ["digonnet"] = new(36.25, -117.20),
        ["pala"] = new(33.365, -117.076),
        ["himalaya mine"] = new(33.20, -116.70),
        ["tourah"] = new(33.55, -114.65),
        ["wave hill"] = new(36.95, -112.00),
        ["antelope canyon"] = new(36.861, -111.374),
        ["horseshoe bend"] = new(36.879, -111.510),
        ["bryce canyon"] = new(37.593, -112.187),
        ["zion"] = new(37.298, -113.026),
        ["arches"] = new(38.733, -109.593),
        ["canyonlands"] = new(38.327, -109.878),
        ["capitol reef"] = new(38.367, -111.261),
        ["great basin"] = new(39.005, -114.220),
        ["white sands"] = new(32.787, -106.326),
        ["carlsbad caverns"] = new(32.175, -104.441),
        ["guadalupe mountains"] = new(31.924, -104.865),
        ["organ mountains"] = new(32.297, -106.565),
        ["chiricahua"] = new(32.006, -109.356),
        ["superstition"] = new(33.480, -111.400),
        ["four peaks"] = new(33.685, -111.326),
        ["havasu"] = new(34.296, -114.138),
        ["lake powell"] = new(37.068, -111.304),
        ["flaming gorge"] = new(40.915, -109.421),
        ["jackson hole"] = new(43.480, -110.762),
        ["tetons"] = new(43.741, -110.802),
        ["glacier"] = new(48.760, -113.787),
        ["badlands"] = new(43.855, -102.340),
        ["black hills"] = new(43.936, -103.720),
        ["mount rushmore"] = new(43.880, -103.459),
        ["devils tower"] = new(44.590, -104.715),
        ["craters of the moon"] = new(43.462, -113.516),
        ["city of rocks"] = new(42.077, -113.702),
        ["sawtooth"] = new(44.140, -115.015),
        ["lassen"] = new(40.488, -121.505),
        ["shasta"] = new(41.409, -122.195),
        ["lassen volcanic"] = new(40.488, -121.505),
        ["redwood"] = new(41.213, -124.005),
        ["sequoia"] = new(36.486, -118.566),
        ["kings canyon"] = new(36.888, -118.555),
        ["yosemite"] = new(37.865, -119.538),
        ["half dome"] = new(37.746, -119.533),
        ["el capitan"] = new(37.734, -119.638),
        ["death valley"] = new(36.505, -117.079),
        ["joshua tree"] = new(33.873, -115.901),
        ["anza-borrego"] = new(33.258, -116.375),
        ["anza borrego"] = new(33.258, -116.375),
        ["channel islands"] = new(34.007, -119.389),
        ["big sur"] = new(36.270, -121.807),
        ["pfeiffer"] = new(36.250, -121.780),
        ["monument valley"] = new(36.998, -110.098),
        ["painted desert"] = new(35.078, -109.782),
        ["petrified forest"] = new(34.910, -109.807),
        ["meteor crater"] = new(35.027, -111.023),
        ["sedona"] = new(34.870, -111.761),
        ["flagstaff"] = new(35.198, -111.651),
        ["grand canyon"] = new(36.054, -112.140),
        ["hoover dam"] = new(36.016, -114.737),
        ["las vegas"] = new(36.170, -115.140),
        ["reno"] = new(39.530, -119.814),
        ["tahoe"] = new(39.096, -120.032),
        ["virginia city"] = new(39.310, -119.650),
        ["tonopah"] = new(38.067, -117.230),
        ["elko"] = new(40.833, -115.763),
        ["wendover"] = new(40.739, -114.037),
        ["salt flats"] = new(40.740, -113.990),
        ["bonneville"] = new(40.763, -113.894),
        ["moab"] = new(38.573, -109.550),
        ["bluff"] = new(37.284, -109.553),
        ["mexican hat"] = new(37.150, -109.867),
        ["page"] = new(36.915, -111.456),
        ["kanab"] = new(37.048, -112.526),
        ["st george"] = new(37.096, -113.568),
        ["cedar city"] = new(37.677, -113.062),
        ["provo"] = new(40.234, -111.659),
        ["salt lake city"] = new(40.761, -111.891),
        ["denver"] = new(39.739, -104.990),
        ["boulder"] = new(40.015, -105.271),
        ["colorado springs"] = new(38.834, -104.821),
        ["pueblo"] = new(38.254, -104.609),
        ["durango"] = new(37.275, -107.880),
        ["telluride"] = new(37.937, -107.812),
        ["aspen"] = new(39.191, -106.817),
        ["vail"] = new(39.640, -106.374),
        ["albuquerque"] = new(35.085, -106.651),
        ["santa fe"] = new(35.687, -105.938),
        ["taos"] = new(36.407, -105.573),
        ["las cruces"] = new(32.320, -106.763),
        ["el paso"] = new(31.761, -106.485),
        ["tucson"] = new(32.222, -110.974),
        ["phoenix"] = new(33.448, -112.074),
        ["yuma"] = new(32.692, -114.628),
        ["quartzsite"] = new(33.664, -114.230),
        ["bisbee"] = new(31.448, -109.928),
        ["tombstone"] = new(31.713, -110.067),
        ["prescott"] = new(34.540, -112.469),
        ["jerome"] = new(34.749, -112.114),
        ["boise"] = new(43.615, -116.202),
        ["twin falls"] = new(42.563, -114.461),
        ["idaho falls"] = new(43.492, -112.041),
        ["missoula"] = new(46.872, -113.994),
        ["helena"] = new(46.596, -112.027),
        ["billings"] = new(45.783, -108.501),
        ["cheyenne"] = new(41.140, -104.820),
        ["casper"] = new(42.867, -106.313),
        ["rapid city"] = new(44.081, -103.231),
        ["sioux falls"] = new(43.547, -96.728),
        ["omaha"] = new(41.257, -95.995),
        ["kansas city"] = new(39.100, -94.578),
        ["oklahoma city"] = new(35.468, -97.516),
        ["tulsa"] = new(36.154, -95.993),
        ["dallas"] = new(32.777, -96.797),
        ["houston"] = new(29.760, -95.370),
        ["austin"] = new(30.267, -97.743),
        ["san antonio"] = new(29.424, -98.494),
        ["big bend"] = new(29.250, -103.250),
        ["marfa"] = new(30.309, -104.021),
        ["crater of diamonds"] = new(34.033, -93.673),
        ["hot springs"] = new(34.504, -93.055),
        ["herkimer"] = new(43.026, -74.986),
        ["keweenaw"] = new(47.383, -88.167),
        ["ishpeming"] = new(46.489, -87.668),
        ["marquette"] = new(46.544, -87.395),
        // Yellowstone NP vs Yellowstone River (MT agate corridor) — longer key wins in TryResolve.
        ["yellowstone"] = new(44.600, -110.500),
        ["yellowstone national park"] = new(44.600, -110.500),
        ["yellowstone river"] = new(46.408, -105.840), // Miles City stretch — classic MT moss/agate hunting
        ["miles city"] = new(46.408, -105.840),
        ["glendive"] = new(47.105, -104.712),
        ["forsyth"] = new(46.266, -106.678),
        ["terry mt"] = new(46.793, -105.312),
        ["old faithful"] = new(44.460, -110.828),
        ["bend"] = new(44.058, -121.315),
        ["burns"] = new(43.586, -119.054),
        ["lakeview"] = new(42.189, -120.346),
        ["klamath falls"] = new(42.225, -121.782),
        ["medford"] = new(42.327, -122.876),
        ["eugene"] = new(44.052, -123.087),
        ["portland"] = new(45.515, -122.679),
        ["seattle"] = new(47.606, -122.332),
        ["spokane"] = new(47.659, -117.426),
        ["olympic"] = new(47.802, -123.604),
        ["mount rainier"] = new(46.853, -121.760),
        ["mount hood"] = new(45.374, -121.696),
        ["crater lake"] = new(42.944, -122.109),
        ["san francisco"] = new(37.775, -122.419),
        ["los angeles"] = new(34.052, -118.244),
        ["san diego"] = new(32.716, -117.161),
        ["sacramento"] = new(38.582, -121.494),
        ["fresno"] = new(36.738, -119.787),
        ["bakersfield"] = new(35.373, -119.019),
        ["palm springs"] = new(33.830, -116.545),
        ["barstow"] = new(34.899, -117.023),
        ["needles"] = new(34.848, -114.614),
        ["kingman"] = new(35.189, -114.053),
        ["williams"] = new(35.249, -112.191),
        ["winslow"] = new(35.024, -110.697),
        ["gallup"] = new(35.528, -108.743),
        ["farmington"] = new(36.728, -108.219),
        // State centroids (broad leads)
        ["oregon"] = new(43.804, -120.554),
        ["arizona"] = new(34.048, -111.094),
        ["utah"] = new(39.321, -111.094),
        ["nevada"] = new(38.803, -116.419),
        ["california"] = new(36.778, -119.418),
        ["new mexico"] = new(34.520, -105.870),
        ["idaho"] = new(44.068, -114.742),
        ["montana"] = new(46.880, -110.363),
        ["wyoming"] = new(43.076, -107.290),
        ["colorado"] = new(39.550, -105.782),
        ["texas"] = new(31.968, -99.902),
        ["washington"] = new(47.751, -120.740),
        ["alaska"] = new(64.201, -149.494),
        ["hawaii"] = new(19.897, -155.583),
    };

    public static int EntryCount => Places.Count;

    public static GeoCoordinate? TryResolve(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var normalized = text.Trim();
        if (Places.TryGetValue(normalized, out var exact))
            return exact;

        foreach (var prefix in new[] { "Place mention: ", "Audio: ", "OCR: ", "Road sign: " })
        {
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                normalized = normalized[prefix.Length..].Trim();
        }

        // Strip quotes
        normalized = normalized.Trim('"', '\'', '“', '”');

        if (Places.TryGetValue(normalized, out exact))
            return exact;

        // Prefer longer name matches to reduce false positives on short tokens
        GeoCoordinate? best = null;
        var bestLen = 0;
        foreach (var (name, coord) in Places)
        {
            if (name.Length < 4) continue;
            if (normalized.Contains(name, StringComparison.OrdinalIgnoreCase) ||
                (normalized.Length >= 4 && name.Contains(normalized, StringComparison.OrdinalIgnoreCase)))
            {
                if (name.Length > bestLen)
                {
                    bestLen = name.Length;
                    best = coord;
                }
            }
        }

        if (best is not null)
            return best;

        var parts = normalized.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon) &&
            lat is >= -90 and <= 90 && lon is >= -180 and <= 180)
        {
            return new GeoCoordinate(lat, lon);
        }

        return null;
    }
}