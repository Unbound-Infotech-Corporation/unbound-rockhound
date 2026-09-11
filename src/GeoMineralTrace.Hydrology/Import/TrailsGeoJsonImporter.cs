using GeoMineralTrace.Core.Geo;
using GeoMineralTrace.Core.Trails;
using GeoMineralTrace.Hydrology.Geo;
using GeoMineralTrace.Hydrology.Storage;
using System.Text.Json;

namespace GeoMineralTrace.Hydrology.Import;

/// <summary>
/// Imports named hiking / access trails from GeoJSON (offline file only).
/// Accepts USGS National Map Trails, USFS trail extracts, and Unbound seed GeoJSON.
/// </summary>
public sealed class TrailsGeoJsonImporter
{
    public sealed record ImportResult(int FeaturesRead, int Imported, int SkippedUnnamed, int SkippedGeometry);

    public async Task<ImportResult> ImportFileAsync(
        string path,
        TrailStore store,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = doc.RootElement;

        if (!root.TryGetProperty("features", out var features))
            throw new InvalidDataException("GeoJSON FeatureCollection missing 'features' array.");

        var imported = 0;
        var skippedUnnamed = 0;
        var skippedGeometry = 0;
        var count = features.GetArrayLength();

        await store.BeginBulkWriteAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var i = 0;
            foreach (var feature in features.EnumerateArray())
            {
                i++;
                if (i % 500 == 0)
                    progress?.Report($"Trails: {i}/{count} features…");

                if (!TryParseFeature(feature, out var trail))
                {
                    if (IsUnnamed(feature))
                        skippedUnnamed++;
                    else
                        skippedGeometry++;
                    continue;
                }

                await store.UpsertAsync(trail, cancellationToken).ConfigureAwait(false);
                imported++;
            }

            await store.EndBulkWriteAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            try { await store.EndBulkWriteAsync(cancellationToken).ConfigureAwait(false); } catch { /* rollback */ }
            throw;
        }

        progress?.Report($"Trail import complete: {imported} trails from {count} features.");
        return new ImportResult(count, imported, skippedUnnamed, skippedGeometry);
    }

    private static bool IsUnnamed(JsonElement feature)
    {
        if (!feature.TryGetProperty("properties", out var props))
            return true;
        return string.IsNullOrWhiteSpace(GetName(props));
    }

    private static bool TryParseFeature(JsonElement feature, out Trail trail)
    {
        trail = null!;
        if (!feature.TryGetProperty("properties", out var props))
            return false;

        var name = GetName(props);
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (!TryGetLineCoordinates(feature, out var vertices) || vertices.Count < 2)
            return false;

        vertices = SimplifyVertices(vertices, maxPoints: 200);
        var lengthKm = ComputePathLengthKm(vertices);
        // Short dig-access spurs matter; keep a low floor.
        if (lengthKm < 0.05)
            return false;

        var permanentId = GetString(
            props,
            "permanent_identifier", "PERMANENT_IDENTIFIER",
            "trail_id", "TRAIL_ID", "TRAIL_NO", "trailno",
            "feature_id", "FEATURE_ID", "objectid", "OBJECTID",
            "id", "ID");
        if (string.IsNullOrWhiteSpace(permanentId) && feature.TryGetProperty("id", out var fid))
            permanentId = fid.ToString();
        permanentId ??= Guid.NewGuid().ToString("N");

        var externalId = BuildExternalId(permanentId, props);
        var state = InferStateCode(props) ?? "US";
        var county = GetString(props, "county", "COUNTY", "COUNTY_NAME");
        var kind = InferKind(name, props);
        var notes = GetString(props, "notes", "NOTES", "comment", "COMMENT", "description", "DESCRIPTION", "remarks");
        var (dataset, sources) = InferSource(props);

        trail = new Trail
        {
            Id = Guid.NewGuid(),
            ExternalId = externalId,
            Name = name.Trim(),
            StateCode = state,
            County = county,
            Kind = kind,
            Vertices = vertices,
            Centroid = TrailStore.ComputeCentroid(vertices),
            LengthKm = lengthKm,
            SourceDataset = dataset,
            Sources = sources,
            Notes = notes
        };
        return true;
    }

    private static string BuildExternalId(string permanentId, JsonElement props)
    {
        if (permanentId.StartsWith("seed-", StringComparison.OrdinalIgnoreCase))
            return $"seed:{permanentId[5..]}";
        if (permanentId.StartsWith("seed:", StringComparison.OrdinalIgnoreCase)
            || permanentId.StartsWith("tnm:", StringComparison.OrdinalIgnoreCase)
            || permanentId.StartsWith("fs:", StringComparison.OrdinalIgnoreCase)
            || permanentId.StartsWith("trail:", StringComparison.OrdinalIgnoreCase))
            return permanentId;

        var sourceHint = GetString(props, "source", "SOURCE", "data_source", "DATA_SOURCE", "agency", "AGENCY") ?? "";
        if (sourceHint.Contains("Forest", StringComparison.OrdinalIgnoreCase)
            || sourceHint.Contains("USFS", StringComparison.OrdinalIgnoreCase)
            || GetString(props, "forestname", "FORESTNAME", "FOREST_NAME") is not null)
            return $"fs:{permanentId}";

        if (sourceHint.Contains("National Map", StringComparison.OrdinalIgnoreCase)
            || sourceHint.Contains("TNM", StringComparison.OrdinalIgnoreCase)
            || GetString(props, "tnm_id", "TNM_ID") is not null)
            return $"tnm:{permanentId}";

        return $"trail:{permanentId}";
    }

    private static (string Dataset, IReadOnlyList<string> Sources) InferSource(JsonElement props)
    {
        var sourceHint = GetString(props, "source", "SOURCE", "data_source", "DATA_SOURCE", "agency", "AGENCY") ?? "";
        if (sourceHint.Contains("Forest", StringComparison.OrdinalIgnoreCase)
            || sourceHint.Contains("USFS", StringComparison.OrdinalIgnoreCase)
            || GetString(props, "forestname", "FORESTNAME") is not null)
        {
            return ("USFS Trails", ["USDA Forest Service trails"]);
        }

        if (sourceHint.Contains("National Map", StringComparison.OrdinalIgnoreCase)
            || sourceHint.Contains("TNM", StringComparison.OrdinalIgnoreCase)
            || GetString(props, "tnm_id", "TNM_ID") is not null)
        {
            return ("USGS National Map Trails", ["USGS The National Map — Trails"]);
        }

        if (!string.IsNullOrWhiteSpace(sourceHint))
            return (sourceHint.Trim(), [sourceHint.Trim()]);

        return ("Trail GeoJSON", ["Local trail GeoJSON import"]);
    }

    private static string? GetName(JsonElement props) =>
        GetString(
            props,
            "name", "NAME",
            "trail_name", "TRAIL_NAME", "TRAILNAME", "trailname",
            "official_trail_name", "OfficialTrailName", "OFFICIAL_TRAIL_NAME",
            "gnis_name", "GNIS_NAME",
            "feature_name", "FEATURE_NAME");

    private static string? GetString(JsonElement props, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (props.TryGetProperty(key, out var val))
            {
                var s = val.ValueKind == JsonValueKind.String ? val.GetString() : val.ToString();
                if (!string.IsNullOrWhiteSpace(s))
                    return s.Trim();
            }
        }

        return null;
    }

    private static string? InferStateCode(JsonElement props)
    {
        var raw = GetString(props, "state", "STATE", "st", "ST", "state_abbr", "STATE_ABBR", "state_code", "STATE_CODE");
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        raw = raw.Trim().ToUpperInvariant();
        return raw.Length == 2 ? raw : null;
    }

    private static TrailKind InferKind(string name, JsonElement props)
    {
        var typeRaw = GetString(
            props,
            "trail_type", "TRAIL_TYPE", "trailtype", "TRAILTYPE",
            "feature_type", "FEATURE_TYPE",
            "rte_type", "RTE_TYPE", "route_type", "ROUTE_TYPE",
            "kind", "KIND", "use", "USE", "primary_use", "PRIMARY_USE") ?? "";

        var blob = $"{typeRaw} {name}";
        if (blob.Contains("OHV", StringComparison.OrdinalIgnoreCase)
            || blob.Contains("ATV", StringComparison.OrdinalIgnoreCase)
            || blob.Contains("motor", StringComparison.OrdinalIgnoreCase))
            return TrailKind.OHV;
        if (blob.Contains("road", StringComparison.OrdinalIgnoreCase)
            || blob.Contains("access", StringComparison.OrdinalIgnoreCase)
            || blob.Contains("4WD", StringComparison.OrdinalIgnoreCase)
            || blob.Contains("jeep", StringComparison.OrdinalIgnoreCase))
            return TrailKind.AccessRoad;
        if (blob.Contains("pack", StringComparison.OrdinalIgnoreCase)
            || blob.Contains("stock", StringComparison.OrdinalIgnoreCase)
            || blob.Contains("equestrian", StringComparison.OrdinalIgnoreCase))
            return TrailKind.Pack;
        if (blob.Contains("hike", StringComparison.OrdinalIgnoreCase)
            || blob.Contains("foot", StringComparison.OrdinalIgnoreCase)
            || blob.Contains("pedestrian", StringComparison.OrdinalIgnoreCase)
            || blob.Contains("trail", StringComparison.OrdinalIgnoreCase))
            return TrailKind.Hiking;

        return TrailKind.Hiking;
    }

    private static bool TryGetLineCoordinates(JsonElement feature, out List<GeoCoordinate> vertices)
    {
        vertices = [];
        if (!feature.TryGetProperty("geometry", out var geom))
            return false;

        var type = geom.TryGetProperty("type", out var t) ? t.GetString() : null;
        if (!geom.TryGetProperty("coordinates", out var coords))
            return false;

        if (string.Equals(type, "LineString", StringComparison.OrdinalIgnoreCase))
        {
            AppendLineString(coords, vertices);
            return vertices.Count >= 2;
        }

        if (string.Equals(type, "MultiLineString", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var line in coords.EnumerateArray())
                AppendLineString(line, vertices);
            return vertices.Count >= 2;
        }

        return false;
    }

    private static void AppendLineString(JsonElement coords, List<GeoCoordinate> vertices)
    {
        foreach (var point in coords.EnumerateArray())
        {
            if (point.GetArrayLength() < 2)
                continue;
            var lon = point[0].GetDouble();
            var lat = point[1].GetDouble();
            if (lat is >= -90 and <= 90 && lon is >= -180 and <= 180)
                vertices.Add(new GeoCoordinate(lat, lon));
        }
    }

    private static List<GeoCoordinate> SimplifyVertices(IReadOnlyList<GeoCoordinate> vertices, int maxPoints)
    {
        if (vertices.Count <= maxPoints)
            return vertices.ToList();

        var result = new List<GeoCoordinate>(maxPoints);
        var step = (double)(vertices.Count - 1) / (maxPoints - 1);
        for (var i = 0; i < maxPoints; i++)
        {
            var idx = (int)Math.Round(i * step);
            idx = Math.Clamp(idx, 0, vertices.Count - 1);
            result.Add(vertices[idx]);
        }

        return result;
    }

    private static double ComputePathLengthKm(IReadOnlyList<GeoCoordinate> vertices)
    {
        var total = 0.0;
        for (var i = 0; i < vertices.Count - 1; i++)
            total += PolylineDistance.HaversineKm(vertices[i], vertices[i + 1]);
        return total;
    }
}
