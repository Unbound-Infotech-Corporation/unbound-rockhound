namespace GeoMineralTrace.Rockhounding.Import;

/// <summary>Normalizes U.S. state names/abbreviations to two-letter codes.</summary>
internal static class UsStateCodes
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AL"] = "AL", ["ALABAMA"] = "AL",
        ["AK"] = "AK", ["ALASKA"] = "AK",
        ["AZ"] = "AZ", ["ARIZONA"] = "AZ",
        ["AR"] = "AR", ["ARKANSAS"] = "AR",
        ["CA"] = "CA", ["CALIFORNIA"] = "CA",
        ["CO"] = "CO", ["COLORADO"] = "CO",
        ["CT"] = "CT", ["CONNECTICUT"] = "CT",
        ["DE"] = "DE", ["DELAWARE"] = "DE",
        ["DC"] = "DC", ["DISTRICT OF COLUMBIA"] = "DC",
        ["FL"] = "FL", ["FLORIDA"] = "FL",
        ["GA"] = "GA", ["GEORGIA"] = "GA",
        ["HI"] = "HI", ["HAWAII"] = "HI",
        ["ID"] = "ID", ["IDAHO"] = "ID",
        ["IL"] = "IL", ["ILLINOIS"] = "IL",
        ["IN"] = "IN", ["INDIANA"] = "IN",
        ["IA"] = "IA", ["IOWA"] = "IA",
        ["KS"] = "KS", ["KANSAS"] = "KS",
        ["KY"] = "KY", ["KENTUCKY"] = "KY",
        ["LA"] = "LA", ["LOUISIANA"] = "LA",
        ["ME"] = "ME", ["MAINE"] = "ME",
        ["MD"] = "MD", ["MARYLAND"] = "MD",
        ["MA"] = "MA", ["MASSACHUSETTS"] = "MA",
        ["MI"] = "MI", ["MICHIGAN"] = "MI",
        ["MN"] = "MN", ["MINNESOTA"] = "MN",
        ["MS"] = "MS", ["MISSISSIPPI"] = "MS",
        ["MO"] = "MO", ["MISSOURI"] = "MO",
        ["MT"] = "MT", ["MONTANA"] = "MT",
        ["NE"] = "NE", ["NEBRASKA"] = "NE",
        ["NV"] = "NV", ["NEVADA"] = "NV",
        ["NH"] = "NH", ["NEW HAMPSHIRE"] = "NH",
        ["NJ"] = "NJ", ["NEW JERSEY"] = "NJ",
        ["NM"] = "NM", ["NEW MEXICO"] = "NM",
        ["NY"] = "NY", ["NEW YORK"] = "NY",
        ["NC"] = "NC", ["NORTH CAROLINA"] = "NC",
        ["ND"] = "ND", ["NORTH DAKOTA"] = "ND",
        ["OH"] = "OH", ["OHIO"] = "OH",
        ["OK"] = "OK", ["OKLAHOMA"] = "OK",
        ["OR"] = "OR", ["OREGON"] = "OR",
        ["PA"] = "PA", ["PENNSYLVANIA"] = "PA",
        ["RI"] = "RI", ["RHODE ISLAND"] = "RI",
        ["SC"] = "SC", ["SOUTH CAROLINA"] = "SC",
        ["SD"] = "SD", ["SOUTH DAKOTA"] = "SD",
        ["TN"] = "TN", ["TENNESSEE"] = "TN",
        ["TX"] = "TX", ["TEXAS"] = "TX",
        ["UT"] = "UT", ["UTAH"] = "UT",
        ["VT"] = "VT", ["VERMONT"] = "VT",
        ["VA"] = "VA", ["VIRGINIA"] = "VA",
        ["WA"] = "WA", ["WASHINGTON"] = "WA",
        ["WV"] = "WV", ["WEST VIRGINIA"] = "WV",
        ["WI"] = "WI", ["WISCONSIN"] = "WI",
        ["WY"] = "WY", ["WYOMING"] = "WY",
        ["PR"] = "PR", ["PUERTO RICO"] = "PR",
        ["VI"] = "VI", ["VIRGIN ISLANDS"] = "VI",
        ["GU"] = "GU", ["GUAM"] = "GU",
        ["AS"] = "AS", ["AMERICAN SAMOA"] = "AS",
        ["MP"] = "MP", ["NORTHERN MARIANA ISLANDS"] = "MP",
    };

    public static bool TryNormalize(string? raw, out string code)
    {
        code = "";
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var key = raw.Trim();
        if (Map.TryGetValue(key, out var found))
        {
            code = found;
            return true;
        }

        return false;
    }

    public static bool IsUnitedStatesCountry(string? country)
    {
        if (string.IsNullOrWhiteSpace(country))
            return false;

        var c = country.Trim();
        return c.Equals("United States", StringComparison.OrdinalIgnoreCase)
               || c.Equals("USA", StringComparison.OrdinalIgnoreCase)
               || c.Equals("US", StringComparison.OrdinalIgnoreCase)
               || c.Equals("U.S.", StringComparison.OrdinalIgnoreCase)
               || c.Equals("U.S.A.", StringComparison.OrdinalIgnoreCase);
    }
}
