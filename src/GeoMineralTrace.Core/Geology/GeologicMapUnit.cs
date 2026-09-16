namespace GeoMineralTrace.Core.Geology;

/// <summary>
/// One Cooperative National Geologic Map unit returned by a point identify.
/// Fields are optional — synthesis coverage and source-map attributes vary.
/// </summary>
public sealed record GeologicMapUnit(
    string? MapUnit,
    string? Name,
    string? Description,
    string? GeoMaterial,
    string? GeoMaterialConfidence,
    string? Age,
    string? MinAge,
    string? MaxAge,
    string? SynthesisMapUnit,
    string? SynthesisMapUnitName,
    string? SynthesisDescription,
    string? MapCitation,
    string? NgmdbUrl,
    string? SynthesisCitation,
    string? SynthesisUrl,
    CngmTheme Theme)
{
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(MapUnit)
        && string.IsNullOrWhiteSpace(Name)
        && string.IsNullOrWhiteSpace(GeoMaterial)
        && string.IsNullOrWhiteSpace(SynthesisMapUnitName);

    public bool LooksUnmapped =>
        ContainsAny(Name, "unmapped")
        || ContainsAny(GeoMaterial, "unmapped")
        || ContainsAny(SynthesisMapUnitName, "unmapped");

    public bool LooksWaterOrIce =>
        ContainsAny(GeoMaterial, "water", "ice")
        || ContainsAny(SynthesisMapUnitName, "water", "ice")
        || ContainsAny(Name, "water or ice", "glacier", "snowfield");

    public bool LooksArtificial =>
        ContainsAny(GeoMaterial, "artificial", "human-engineered", "made ground")
        || ContainsAny(Name, "artificial", "fill", "made land");

    public string SearchText =>
        string.Join(' ',
            new[]
            {
                MapUnit, Name, Description, GeoMaterial, Age, MinAge, MaxAge,
                SynthesisMapUnit, SynthesisMapUnitName, SynthesisDescription
            }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public string DisplayTitle
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(MapUnit))
                return $"{Name} ({MapUnit})";
            if (!string.IsNullOrWhiteSpace(Name))
                return Name;
            if (!string.IsNullOrWhiteSpace(SynthesisMapUnitName) && !string.IsNullOrWhiteSpace(SynthesisMapUnit))
                return $"{SynthesisMapUnitName} ({SynthesisMapUnit})";
            if (!string.IsNullOrWhiteSpace(SynthesisMapUnitName))
                return SynthesisMapUnitName;
            return MapUnit ?? "Geologic unit";
        }
    }

    public string ThemeLabel => Theme switch
    {
        CngmTheme.Quaternary => "Quaternary",
        CngmTheme.PreQuaternary => "Pre-Quaternary",
        CngmTheme.Precambrian => "Precambrian",
        _ => "Earth Surface"
    };

    private static bool ContainsAny(string? text, params string[] needles)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        foreach (var needle in needles)
        {
            if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
