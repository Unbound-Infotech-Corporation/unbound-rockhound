namespace GeoMineralTrace.Core.Geo;

/// <summary>
/// Shared U.S. state code resolution for filters and imports.
/// ComboBox UIs show the 50 states; Normalize also accepts DC and territories.
/// </summary>
public static class UsStateCode
{
    public sealed record State(string Code, string Name)
    {
        public string Display => $"{Name} ({Code})";
    }

    /// <summary>The 50 U.S. states in conventional order (AL–WY). No DC/territories.</summary>
    public static IReadOnlyList<State> States50 { get; } =
    [
        new("AL", "Alabama"),
        new("AK", "Alaska"),
        new("AZ", "Arizona"),
        new("AR", "Arkansas"),
        new("CA", "California"),
        new("CO", "Colorado"),
        new("CT", "Connecticut"),
        new("DE", "Delaware"),
        new("FL", "Florida"),
        new("GA", "Georgia"),
        new("HI", "Hawaii"),
        new("ID", "Idaho"),
        new("IL", "Illinois"),
        new("IN", "Indiana"),
        new("IA", "Iowa"),
        new("KS", "Kansas"),
        new("KY", "Kentucky"),
        new("LA", "Louisiana"),
        new("ME", "Maine"),
        new("MD", "Maryland"),
        new("MA", "Massachusetts"),
        new("MI", "Michigan"),
        new("MN", "Minnesota"),
        new("MS", "Mississippi"),
        new("MO", "Missouri"),
        new("MT", "Montana"),
        new("NE", "Nebraska"),
        new("NV", "Nevada"),
        new("NH", "New Hampshire"),
        new("NJ", "New Jersey"),
        new("NM", "New Mexico"),
        new("NY", "New York"),
        new("NC", "North Carolina"),
        new("ND", "North Dakota"),
        new("OH", "Ohio"),
        new("OK", "Oklahoma"),
        new("OR", "Oregon"),
        new("PA", "Pennsylvania"),
        new("RI", "Rhode Island"),
        new("SC", "South Carolina"),
        new("SD", "South Dakota"),
        new("TN", "Tennessee"),
        new("TX", "Texas"),
        new("UT", "Utah"),
        new("VT", "Vermont"),
        new("VA", "Virginia"),
        new("WA", "Washington"),
        new("WV", "West Virginia"),
        new("WI", "Wisconsin"),
        new("WY", "Wyoming")
    ];

    private static readonly Dictionary<string, string> Lookup = BuildLookup();

    private static Dictionary<string, string> BuildLookup()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var state in States50)
        {
            map[state.Code] = state.Code;
            map[state.Name] = state.Code;
            map[state.Display] = state.Code;
        }

        // Extra jurisdictions accepted when pasted/typed, not shown in the 50-state ComboBox.
        Add(map, "DC", "District of Columbia");
        Add(map, "PR", "Puerto Rico");
        Add(map, "VI", "Virgin Islands");
        Add(map, "GU", "Guam");
        Add(map, "AS", "American Samoa");
        Add(map, "MP", "Northern Mariana Islands");
        return map;
    }

    private static void Add(Dictionary<string, string> map, string code, string name)
    {
        map[code] = code;
        map[name] = code;
        map[$"{name} ({code})"] = code;
    }

    /// <summary>
    /// Resolves a 2-letter state code. Accepts "WA", "Washington", "Washington (WA)".
    /// Returns null for empty / All / unknown values (including made-up 2-letter codes).
    /// </summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var t = raw.Trim();
        if (t.Equals("All", StringComparison.OrdinalIgnoreCase)
            || t.Equals("All states", StringComparison.OrdinalIgnoreCase)
            || t.Equals("All / near home", StringComparison.OrdinalIgnoreCase)
            || t.Equals("All ST", StringComparison.OrdinalIgnoreCase)
            || t.Equals("ST", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var open = t.LastIndexOf('(');
        var close = t.LastIndexOf(')');
        if (open >= 0 && close > open + 1)
        {
            var inside = t[(open + 1)..close].Trim();
            if (Lookup.TryGetValue(inside, out var fromParen))
                return fromParen;
        }

        return Lookup.TryGetValue(t, out var code) ? code : null;
    }
}
