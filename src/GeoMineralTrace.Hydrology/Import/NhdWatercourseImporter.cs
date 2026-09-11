using System.Globalization;
using System.Text.Json;
using GeoMineralTrace.Core.Geo;
using GeoMineralTrace.Core.Hydrology;
using GeoMineralTrace.Hydrology.Geo;
using GeoMineralTrace.Hydrology.Storage;

namespace GeoMineralTrace.Hydrology.Import;

/// <summary>Imports named USGS NHD flowlines from GeoJSON (offline file only).</summary>
public sealed class NhdWatercourseImporter
{
    private static readonly HashSet<int> AllowedFTypes = [460, 558]; // StreamRiver, ArtificialPath
    private static readonly HashSet<int> AllowedFCodes = [33400, 33600, 46003, 46006, 55800]; // common river/creek codes

    public sealed record ImportResult(int FeaturesRead, int Imported, int SkippedUnnamed, int SkippedGeometry);

    public async Task<ImportResult> ImportFileAsync(
        string path,
        RiverStore store,
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
                    progress?.Report($"NHD: {i}/{count} features…");

                if (!TryParseFeature(feature, out var watercourse))
                {
                    if (IsUnnamed(feature))
                        skippedUnnamed++;
                    else
                        skippedGeometry++;
                    continue;
                }

                await store.UpsertAsync(watercourse, cancellationToken).ConfigureAwait(false);
                imported++;
            }

            await store.EndBulkWriteAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            try { await store.EndBulkWriteAsync(cancellationToken).ConfigureAwait(false); } catch { /* rollback */ }
            throw;
        }

        progress?.Report($"NHD import complete: {imported} watercourses from {count} features.");
        return new ImportResult(count, imported, skippedUnnamed, skippedGeometry);
    }

    private static bool IsUnnamed(JsonElement feature)
    {
        if (!feature.TryGetProperty("properties", out var props))
            return true;
        return string.IsNullOrWhiteSpace(GetName(props));
    }

    private static bool TryParseFeature(JsonElement feature, out Watercourse watercourse)
    {
        watercourse = null!;
        if (!feature.TryGetProperty("properties", out var props))
            return false;

        var name = GetName(props);
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (!PassesTypeFilter(props))
            return false;

        if (!TryGetLineCoordinates(feature, out var vertices) || vertices.Count < 2)
            return false;

        vertices = SimplifyVertices(vertices, maxPoints: 200);
        var lengthKm = ComputePathLengthKm(vertices);
        if (lengthKm < 0.5)
            return false;

        var permanentId = GetString(props, "permanent_identifier", "PERMANENT_IDENTIFIER", "nhdplusid", "NHDPlusID");
        if (string.IsNullOrWhiteSpace(permanentId) && feature.TryGetProperty("id", out var fid))
            permanentId = fid.ToString();
        permanentId ??= Guid.NewGuid().ToString("N");
        var externalId = permanentId.StartsWith("seed-", StringComparison.OrdinalIgnoreCase)
            ? $"seed:{permanentId[5..]}"
            : $"nhd:{permanentId}";
        var gnis = GetString(props, "gnis_id", "GNIS_ID", "gnis_name_id");
        var state = InferStateCode(props) ?? "US";
        var county = GetString(props, "county", "COUNTY");
        var kind = InferKind(name, props);

        watercourse = new Watercourse
        {
            Id = Guid.NewGuid(),
            ExternalId = externalId,
            Name = name.Trim(),
            StateCode = state,
            County = county,
            Kind = kind,
            GnisId = gnis,
            Vertices = vertices,
            Centroid = RiverStore.ComputeCentroid(vertices),
            LengthKm = lengthKm,
            SourceDataset = "USGS NHD",
            SourceVintage = "NHD",
            Sources = ["USGS National Hydrography Dataset"]
        };
        return true;
    }

    private static bool PassesTypeFilter(JsonElement props)
    {
        if (props.TryGetProperty("ftype", out var ft) && ft.TryGetInt32(out var ftype))
        {
            if (!AllowedFTypes.Contains(ftype))
                return false;
        }
        else if (props.TryGetProperty("FType", out var ft2) && ft2.TryGetInt32(out var ftype2))
        {
            if (!AllowedFTypes.Contains(ftype2))
                return false;
        }

        if (props.TryGetProperty("fcode", out var fc) && fc.TryGetInt32(out var fcode))
        {
            if (AllowedFCodes.Count > 0 && !AllowedFCodes.Contains(fcode))
            {
                // Allow if ftype already passed — fcode sets vary by extract
            }
        }

        return true;
    }

    private static string? GetName(JsonElement props)
    {
        var name = GetString(props, "gnis_name", "GNIS_NAME", "name", "NAME", "gnis_nm", "GNIS_NM");
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

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
        var raw = GetString(props, "state", "STATE", "st", "ST", "state_abbr");
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        raw = raw.Trim().ToUpperInvariant();
        return raw.Length == 2 ? raw : null;
    }

    private static WatercourseKind InferKind(string name, JsonElement props)
    {
        var ftypeName = GetString(props, "ftype_name", "FTypeName") ?? "";
        if (ftypeName.Contains("Canal", StringComparison.OrdinalIgnoreCase))
            return WatercourseKind.Canal;
        if (name.Contains(" Creek", StringComparison.OrdinalIgnoreCase) ||
            name.Contains(" Crk", StringComparison.OrdinalIgnoreCase))
            return WatercourseKind.Creek;
        if (name.Contains(" River", StringComparison.OrdinalIgnoreCase))
            return WatercourseKind.River;
        if (name.Contains(" Stream", StringComparison.OrdinalIgnoreCase))
            return WatercourseKind.Stream;
        return WatercourseKind.Stream;
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
