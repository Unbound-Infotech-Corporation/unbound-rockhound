using System.Globalization;
using System.Text;
using System.Text.Json;
using GeoMineralTrace.Core.Claims;
using GeoMineralTrace.Core.Geo;
using GeoMineralTrace.Claims.Storage;

namespace GeoMineralTrace.Claims.Import;

/// <summary>
/// Imports BLM MLRS mining-claim extracts from local GeoJSON or CSV files only (no network).
/// Official bulk sources: MLRS FeatureServer / ArcGIS Hub exports; reports.blm.gov Geographic Index CSVs.
/// </summary>
public sealed class BlmMlrsClaimImporter
{
    public const string SourceName = "BLM MLRS";

    public sealed record ImportResult(
        int TotalFeatures,
        int Upserted,
        int SkippedNoSerial,
        int SkippedNoCoords,
        IReadOnlyDictionary<string, int> CountsByState,
        IReadOnlyDictionary<string, int> CountsByStatus,
        DateTimeOffset ImportedUtc);

    public async Task<ImportResult> ImportFileAsync(
        string path,
        ClaimStore store,
        DateOnly? asOf = null,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null,
        bool validateSanity = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
            throw new FileNotFoundException("BLM claims file not found.", path);

        var asOfDate = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var importedUtc = DateTimeOffset.UtcNow;
        var ext = Path.GetExtension(path).ToLowerInvariant();

        IReadOnlyList<MiningClaim> claims = ext switch
        {
            ".geojson" or ".json" => ParseGeoJson(await File.ReadAllTextAsync(path, cancellationToken), asOfDate, importedUtc),
            ".csv" => ParseCsv(await File.ReadAllTextAsync(path, cancellationToken), asOfDate, importedUtc),
            _ => throw new InvalidDataException($"Unsupported claims file type: {ext}. Use .geojson or .csv.")
        };

        var result = await UpsertAllAsync(claims, store, cancellationToken, progress).ConfigureAwait(false);
        if (validateSanity)
            ClaimImportSanity.ValidateBulkImport(path, result.TotalFeatures, result.Upserted);
        return result;
    }

    public IReadOnlyList<MiningClaim> ParseGeoJson(string json, DateOnly asOf, DateTimeOffset importedUtc)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("features", out var features) ||
            features.ValueKind != JsonValueKind.Array)
            return [];

        var list = new List<MiningClaim>();
        foreach (var feature in features.EnumerateArray())
        {
            try
            {
                var claim = MapGeoJsonFeature(feature, asOf, importedUtc);
                if (claim is not null)
                    list.Add(claim);
            }
            catch (Exception ex)
            {
                // Skip malformed features rather than aborting a multi-GB national import.
                System.Diagnostics.Debug.WriteLine($"BLM feature map failed: {ex.Message}");
            }
        }

        return list;
    }

    public IReadOnlyList<MiningClaim> ParseCsv(string csvText, DateOnly asOf, DateTimeOffset importedUtc)
    {
        var lines = csvText.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2)
            return [];

        var header = SplitCsv(lines[0]).Select(h => h.Trim().Trim('"').ToLowerInvariant().Replace(' ', '_')).ToArray();
        var list = new List<MiningClaim>();
        for (var i = 1; i < lines.Length; i++)
        {
            var cols = SplitCsv(lines[i]);
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var c = 0; c < header.Length && c < cols.Count; c++)
                row[header[c]] = cols[c].Trim().Trim('"');

            var claim = MapCsvRow(row, asOf, importedUtc);
            if (claim is not null)
                list.Add(claim);
        }

        return list;
    }

    private static async Task<ImportResult> UpsertAllAsync(
        IReadOnlyList<MiningClaim> claims,
        ClaimStore store,
        CancellationToken cancellationToken,
        IProgress<string>? progress)
    {
        var byState = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var byStatus = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var skippedSerial = 0;
        var skippedCoords = 0;
        var upserted = 0;

        await store.BeginBulkWriteAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var batch = 0;
            foreach (var claim in claims)
            {
                if (string.IsNullOrWhiteSpace(claim.SerialNumber))
                {
                    skippedSerial++;
                    continue;
                }

                if (claim.Coordinates is null)
                    skippedCoords++; // still upsert — legal description may be enough

                await store.UpsertAsync(claim, cancellationToken).ConfigureAwait(false);
                upserted++;
                byState[claim.StateCode] = byState.GetValueOrDefault(claim.StateCode) + 1;
                var statusKey = ClaimStatusClassifier.StatusBadge(claim.Status);
                byStatus[statusKey] = byStatus.GetValueOrDefault(statusKey) + 1;

                batch++;
                if (batch % 2000 == 0)
                {
                    await store.EndBulkWriteAsync(cancellationToken).ConfigureAwait(false);
                    await store.BeginBulkWriteAsync(cancellationToken).ConfigureAwait(false);
                    progress?.Report($"Upserted {upserted:N0}…");
                }
            }
        }
        finally
        {
            await store.EndBulkWriteAsync(cancellationToken).ConfigureAwait(false);
        }

        progress?.Report($"Done. Upserted {upserted:N0} claims.");
        return new ImportResult(
            claims.Count,
            upserted,
            skippedSerial,
            skippedCoords,
            byState,
            byStatus,
            DateTimeOffset.UtcNow);
    }

    private static MiningClaim? MapGeoJsonFeature(JsonElement feature, DateOnly asOf, DateTimeOffset importedUtc)
    {
        if (!feature.TryGetProperty("properties", out var props))
            return null;

        var serial = GetString(props, "CSE_NR", "serial_number", "serial");
        if (string.IsNullOrWhiteSpace(serial))
            return null;

        var name = GetString(props, "CSE_NAME", "claim_name", "name");
        if (string.IsNullOrWhiteSpace(name))
            name = serial;

        var disposition = GetString(props, "CSE_DISP", "disposition", "status");
        var product = GetString(props, "BLM_PROD", "claim_type", "product");
        var meta = GetString(props, "CSE_META", "legal_description");
        var legacy = GetString(props, "LEG_CSE_NR", "legacy_serial");
        var acres = GetDouble(props, "RCRD_ACRS", "acres");
        var createdMs = GetLong(props, "Created", "created");
        var locationDate = createdMs is { } ms && ms > 0
            ? DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime)
            : (DateOnly?)null;

        var centroid = TryCentroid(feature);
        var plss = ParsePlss(meta);
        var state = InferState(serial, plss.StateCode, centroid);

        var lastFee = ParseOptionalDate(GetString(props, "last_maintenance_fee_paid", "last_fee_paid"));
        var lastAssessment = ParseOptionalInt(GetString(props, "last_assessment_year", "assessment_year"));
        var claimant = NullIfEmpty(GetString(props, "claimant", "claimant_of_record"));
        var fieldOffice = NullIfEmpty(GetString(props, "field_office", "admin_office"));
        var minerals = ParseMinerals(GetString(props, "minerals", "commodity"));

        var status = ClaimStatusClassifier.Classify(disposition, lastFee, lastAssessment, asOf);
        var deadlineApproaching = ClaimStatusClassifier.MaintenanceDeadlineApproaching(
            status, lastFee, lastAssessment, asOf);

        var notes = ClaimStatusClassifier.LegalExplainer(status);
        if (deadlineApproaching)
            notes += " Maintenance-fee deadline (Sept 1) is approaching — verify payment status in MLRS; fee history was not in this extract.";

        return new MiningClaim
        {
            Id = Guid.NewGuid(),
            ExternalId = $"blm-mlrs:{serial.Trim()}",
            ClaimName = name.Trim(),
            SerialNumber = serial.Trim(),
            LegacySerialNumber = NullIfEmpty(legacy),
            ClaimType = MapClaimType(product),
            Status = status,
            Coordinates = centroid,
            LegalDescription = NullIfEmpty(meta),
            Township = plss.Township,
            Range = plss.Range,
            Section = plss.Section,
            Meridian = plss.Meridian,
            ClaimantOfRecord = claimant,
            LocationDate = locationDate,
            LastMaintenanceFeePaid = lastFee,
            LastAssessmentYear = lastAssessment,
            StateCode = state,
            FieldOffice = fieldOffice,
            Minerals = minerals,
            Acres = acres,
            SourceDataset = SourceName,
            SourceImportedUtc = importedUtc,
            BlmCaseDisposition = NullIfEmpty(disposition),
            MaintenanceDeadlineApproaching = deadlineApproaching,
            LegalNotes = notes
        };
    }

    private static MiningClaim? MapCsvRow(Dictionary<string, string> row, DateOnly asOf, DateTimeOffset importedUtc)
    {
        string G(params string[] keys)
        {
            foreach (var k in keys)
            {
                if (row.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v))
                    return v;
            }

            return "";
        }

        var serial = G("serial_number", "cse_nr", "serial", "blm_serial");
        if (string.IsNullOrWhiteSpace(serial))
            return null;

        var name = G("claim_name", "cse_name", "name");
        if (string.IsNullOrWhiteSpace(name))
            name = serial;

        var disposition = G("status", "cse_disp", "disposition", "case_disposition");
        var product = G("claim_type", "blm_prod", "type");
        var meta = G("legal_description", "cse_meta", "plss");
        var stateRaw = G("state", "state_code", "geographical_state");
        var latRaw = G("latitude", "lat", "y");
        var lonRaw = G("longitude", "lon", "long", "x");
        GeoCoordinate? coords = null;
        if (double.TryParse(latRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) &&
            double.TryParse(lonRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon) &&
            lat is >= -90 and <= 90 && lon is >= -180 and <= 180 &&
            !(Math.Abs(lat) < 1e-9 && Math.Abs(lon) < 1e-9))
            coords = new GeoCoordinate(lat, lon);

        var plss = ParsePlss(meta);
        var state = !string.IsNullOrWhiteSpace(stateRaw)
            ? NormalizeState(stateRaw)
            : InferState(serial, plss.StateCode, coords);

        var lastFee = ParseOptionalDate(G("last_maintenance_fee_paid", "last_fee_paid", "last_assessment_paid"));
        var lastAssessment = ParseOptionalInt(G("last_assessment_year", "assessment_year"));
        var locationDate = ParseOptionalDate(G("location_date", "located", "created"));

        var status = ClaimStatusClassifier.Classify(disposition, lastFee, lastAssessment, asOf);
        // Allow CSV to force ExpiringSoon via status column.
        if (disposition.Contains("expir", StringComparison.OrdinalIgnoreCase))
            status = ClaimStatus.ExpiringSoon;

        var deadlineApproaching = ClaimStatusClassifier.MaintenanceDeadlineApproaching(
            status, lastFee, lastAssessment, asOf);

        return new MiningClaim
        {
            Id = Guid.NewGuid(),
            ExternalId = $"blm-mlrs:{serial.Trim()}",
            ClaimName = name.Trim(),
            SerialNumber = serial.Trim(),
            LegacySerialNumber = NullIfEmpty(G("legacy_serial", "leg_cse_nr")),
            ClaimType = MapClaimType(product),
            Status = status,
            Coordinates = coords,
            LegalDescription = NullIfEmpty(meta),
            Township = NullIfEmpty(G("township")) ?? plss.Township,
            Range = NullIfEmpty(G("range")) ?? plss.Range,
            Section = NullIfEmpty(G("section")) ?? plss.Section,
            Meridian = NullIfEmpty(G("meridian")) ?? plss.Meridian,
            ClaimantOfRecord = NullIfEmpty(G("claimant", "claimant_of_record", "customer")),
            LocationDate = locationDate,
            LastMaintenanceFeePaid = lastFee,
            LastAssessmentYear = lastAssessment,
            StateCode = state,
            County = NullIfEmpty(G("county")),
            FieldOffice = NullIfEmpty(G("field_office", "admin_office", "office")),
            Minerals = ParseMinerals(G("minerals", "commodity")),
            Acres = double.TryParse(G("acres", "rcrd_acrs"), NumberStyles.Float, CultureInfo.InvariantCulture, out var a) ? a : null,
            SourceDataset = SourceName,
            SourceImportedUtc = importedUtc,
            BlmCaseDisposition = NullIfEmpty(disposition),
            MaintenanceDeadlineApproaching = deadlineApproaching,
            LegalNotes = ClaimStatusClassifier.LegalExplainer(status)
        };
    }

    private static GeoCoordinate? TryCentroid(JsonElement feature)
    {
        if (!feature.TryGetProperty("geometry", out var geom) || geom.ValueKind == JsonValueKind.Null)
            return null;

        if (!geom.TryGetProperty("coordinates", out var coords))
            return null;

        var pts = new List<(double Lon, double Lat)>();
        CollectPositions(coords, pts);
        if (pts.Count == 0)
            return null;

        var lon = pts.Average(p => p.Lon);
        var lat = pts.Average(p => p.Lat);
        if (lat is < -90 or > 90 || lon is < -180 or > 180)
            return null;
        return new GeoCoordinate(lat, lon);
    }

    private static void CollectPositions(JsonElement el, List<(double Lon, double Lat)> sink)
    {
        if (el.ValueKind != JsonValueKind.Array || el.GetArrayLength() == 0)
            return;

        var first = el[0];
        if (first.ValueKind == JsonValueKind.Number && el.GetArrayLength() >= 2)
        {
            sink.Add((el[0].GetDouble(), el[1].GetDouble()));
            return;
        }

        foreach (var child in el.EnumerateArray())
            CollectPositions(child, sink);
    }

    private readonly record struct PlssParts(string? StateCode, string? Meridian, string? Township, string? Range, string? Section);

    private static PlssParts ParsePlss(string? meta)
    {
        if (string.IsNullOrWhiteSpace(meta))
            return default;

        // Example: "NV 21 0230N 0440E 022 A SWSE" (pipe-separated multi-legals possible)
        var segments = meta.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
            return default;

        var first = segments[0];
        var parts = first.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 5)
            return default;

        var state = parts[0].Length == 2 ? parts[0].ToUpperInvariant() : null;
        var meridian = parts[1];
        var township = parts[2];
        var range = parts[3];
        var section = parts[4].TrimStart('0');
        if (string.IsNullOrEmpty(section))
            section = parts[4];
        return new PlssParts(state, meridian, township, range, section);
    }

    private static string InferState(string serial, string? plssState, GeoCoordinate? coords)
    {
        if (!string.IsNullOrWhiteSpace(plssState) && plssState.Length == 2)
            return plssState.ToUpperInvariant();

        // Serials often begin with state code: NV105221812
        if (serial.Length >= 2 && char.IsLetter(serial[0]) && char.IsLetter(serial[1]))
            return serial[..2].ToUpperInvariant();

        return "XX";
    }

    private static string NormalizeState(string raw)
    {
        var t = raw.Trim();
        if (t.Length == 2)
            return t.ToUpperInvariant();
        return t.ToUpperInvariant() switch
        {
            "NEVADA" => "NV",
            "OREGON" => "OR",
            "CALIFORNIA" => "CA",
            "ARIZONA" => "AZ",
            "UTAH" => "UT",
            "IDAHO" => "ID",
            "MONTANA" => "MT",
            "WYOMING" => "WY",
            "COLORADO" => "CO",
            "NEW MEXICO" => "NM",
            "ALASKA" => "AK",
            "WASHINGTON" => "WA",
            _ => t.Length >= 2 ? t[..2].ToUpperInvariant() : "XX"
        };
    }

    private static ClaimType MapClaimType(string? product)
    {
        var p = (product ?? "").ToLowerInvariant();
        if (p.Contains("lode"))
            return ClaimType.Lode;
        if (p.Contains("placer"))
            return ClaimType.Placer;
        if (p.Contains("mill"))
            return ClaimType.MillSite;
        if (p.Contains("tunnel"))
            return ClaimType.TunnelSite;
        return ClaimType.Unknown;
    }

    private static IReadOnlyList<string> ParseMinerals(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];
        return raw.Split([';', ',', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.ToLowerInvariant())
            .Distinct()
            .OrderBy(x => x)
            .ToList();
    }

    private static string GetString(JsonElement props, params string[] names)
    {
        foreach (var name in names)
        {
            if (props.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String)
                return el.GetString() ?? "";
            // case-insensitive scan
            foreach (var p in props.EnumerateObject())
            {
                if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String)
                    return p.Value.GetString() ?? "";
            }
        }

        return "";
    }

    private static double? GetDouble(JsonElement props, params string[] names)
    {
        foreach (var name in names)
        {
            foreach (var p in props.EnumerateObject())
            {
                if (!p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetDouble(out var d))
                    return d;
                if (p.Value.ValueKind == JsonValueKind.String &&
                    double.TryParse(p.Value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var ds))
                    return ds;
            }
        }

        return null;
    }

    private static long? GetLong(JsonElement props, params string[] names)
    {
        foreach (var name in names)
        {
            foreach (var p in props.EnumerateObject())
            {
                if (!p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt64(out var n))
                    return n;
            }
        }

        return null;
    }

    private static DateOnly? ParseOptionalDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        if (DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d;
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
            return DateOnly.FromDateTime(dt);
        return null;
    }

    private static int? ParseOptionalInt(string? raw) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static List<string> SplitCsv(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        foreach (var ch in line)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (ch == ',' && !inQuotes)
            {
                result.Add(sb.ToString());
                sb.Clear();
                continue;
            }

            sb.Append(ch);
        }

        result.Add(sb.ToString());
        return result;
    }
}
