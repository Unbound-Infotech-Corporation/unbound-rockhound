using System.Text.Json;

namespace GeoMineralTrace.Core.Geology;

/// <summary>
/// Parses ArcGIS FeatureServer query / Identify JSON for CNGM map-unit polygons.
/// </summary>
public static class CngmIdentifyJsonParser
{
    public static GeologicMapUnit? Parse(string? json, CngmTheme theme = CngmTheme.EarthSurface)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            return Parse(doc.RootElement, theme);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static GeologicMapUnit? Parse(JsonElement root, CngmTheme theme = CngmTheme.EarthSurface)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out _))
            return null;

        if (TryGetAttributes(root, out var attrs))
            return FromAttributes(attrs, theme);

        return null;
    }

    private static bool TryGetAttributes(JsonElement root, out JsonElement attrs)
    {
        attrs = default;
        if (root.ValueKind != JsonValueKind.Object)
            return false;

        if (root.TryGetProperty("features", out var features) && features.ValueKind == JsonValueKind.Array)
        {
            foreach (var feature in features.EnumerateArray())
            {
                if (feature.ValueKind == JsonValueKind.Object
                    && feature.TryGetProperty("attributes", out attrs)
                    && attrs.ValueKind == JsonValueKind.Object)
                {
                    return true;
                }
            }
        }

        if (root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
        {
            foreach (var result in results.EnumerateArray())
            {
                if (result.ValueKind == JsonValueKind.Object
                    && result.TryGetProperty("attributes", out attrs)
                    && attrs.ValueKind == JsonValueKind.Object)
                {
                    return true;
                }
            }
        }

        if (root.TryGetProperty("attributes", out attrs) && attrs.ValueKind == JsonValueKind.Object)
            return true;

        return false;
    }

    private static GeologicMapUnit FromAttributes(JsonElement attrs, CngmTheme theme) =>
        new(
            MapUnit: ReadString(attrs, "mapunit", "MapUnit", "source_mapunit"),
            Name: ReadString(attrs, "name", "Name"),
            Description: ReadString(attrs, "description", "Description"),
            GeoMaterial: ReadString(attrs, "geomaterial", "GeoMaterial", "label_geomaterial"),
            GeoMaterialConfidence: ReadString(attrs, "geomaterialconfidence", "GeoMaterialConfidence"),
            Age: ReadString(attrs, "age", "Age"),
            MinAge: ReadString(attrs, "min_age", "Min_Age", "label_min_age"),
            MaxAge: ReadString(attrs, "max_age", "Max_Age", "label_max_age"),
            SynthesisMapUnit: ReadString(attrs, "synthesis_mapunit", "Synthesis_MapUnit"),
            SynthesisMapUnitName: ReadString(attrs, "synthesis_mapunitname", "Synthesis_MapUnitName"),
            SynthesisDescription: ReadString(attrs, "synthesis_description", "Synthesis_Description"),
            MapCitation: ReadString(attrs, "map_citation", "Map_Citation", "label_source"),
            NgmdbUrl: ReadString(attrs, "ngmdb_url", "NGMDB_URL"),
            SynthesisCitation: ReadString(attrs, "synthesis_citation", "Synthesis_Citation"),
            SynthesisUrl: ReadString(attrs, "synthesis_url", "Synthesis_URL"),
            Theme: theme);

    private static string? ReadString(JsonElement attrs, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetPropertyIgnoreCase(attrs, name, out var value))
                continue;

            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    var text = value.GetString();
                    return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
                case JsonValueKind.Number:
                    return value.ToString();
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    continue;
                default:
                    var raw = value.ToString();
                    return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
            }
        }

        return null;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.TryGetProperty(name, out value))
            return true;

        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
