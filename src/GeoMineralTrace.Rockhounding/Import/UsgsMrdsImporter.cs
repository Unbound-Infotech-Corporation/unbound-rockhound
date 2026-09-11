using System.Globalization;
using System.Text;
using GeoMineralTrace.Core.Geo;
using GeoMineralTrace.Core.Rockhounding;
using GeoMineralTrace.Rockhounding.Ownership;
using GeoMineralTrace.Rockhounding.Scoring;
using GeoMineralTrace.Rockhounding.Storage;

namespace GeoMineralTrace.Rockhounding.Import;

/// <summary>
/// Imports USGS MRDS and USGS Critical Minerals occurrence CSVs from local files only (no network).
/// Maps commodity occurrences to conservative locality records — not all are collectable sites.
/// </summary>
public sealed class UsgsMrdsImporter
{
    public const string MrdsSourceName = "USGS MRDS";
    public const string MrdsVintage = "2011";
    public const string MrdsHumanActivityFlag =
        "Human-activity data may be outdated (MRDS last systematically updated 2011)";

    public const string CritMinSourceName = "USGS Critical Minerals";
    public const string CritMinVintage = "2023";
    public const string CritMinHumanActivityFlag =
        "Human-activity / ownership data may be outdated — verify current land and claim status before visiting";

    public sealed record ImportPreview(
        int TotalRows,
        int ImportableRows,
        int SkippedNoCoords,
        int FlaggedRestricted,
        IReadOnlyList<string> SampleNames);

    public sealed record InvalidCoordinateRecord(
        string? ExternalId,
        string? Name,
        string? State,
        string? LatitudeRaw,
        string? LongitudeRaw,
        string Reason);

    public sealed record ImportResult(
        int TotalRowsRead,
        int Upserted,
        int DedupedAgainstExisting,
        int SkippedNonUs,
        int SkippedMissingNameOrState,
        int InvalidCoordinates,
        IReadOnlyDictionary<string, int> CountsByState,
        IReadOnlyList<InvalidCoordinateRecord> InvalidCoordinateSamples,
        string DatasetLabel,
        string SourceVintage);

    public ImportPreview Preview(string csvText)
    {
        var rows = ParseRows(csvText);
        var mapped = new List<Locality>();
        var skippedCoords = 0;
        foreach (var row in rows)
        {
            var outcome = TryMapRow(row, detectKind: null, unitedStatesOnly: false);
            if (outcome.Locality is { } loc)
                mapped.Add(loc);
            else if (outcome.InvalidCoordinate is not null)
                skippedCoords++;
        }

        return new ImportPreview(
            rows.Count,
            mapped.Count,
            skippedCoords,
            mapped.Count(l => l.AccessStatus is AccessStatus.Restricted or AccessStatus.Closed),
            mapped.Take(5).Select(l => l.Name).ToList());
    }

    public IReadOnlyList<Locality> Parse(string csvText) =>
        ParseRows(csvText)
            .Select(r => TryMapRow(r, detectKind: null, unitedStatesOnly: false).Locality)
            .Where(l => l is not null)
            .Cast<Locality>()
            .ToList();

    /// <summary>Legacy entry: upserts all mappable rows; returns upsert count.</summary>
    public async Task<int> ImportFileAsync(
        string csvPath,
        LocalityStore store,
        CancellationToken cancellationToken = default)
    {
        var result = await ImportFileWithStatsAsync(
            csvPath,
            store,
            unitedStatesOnly: false,
            cancellationToken).ConfigureAwait(false);
        return result.Upserted;
    }

    /// <summary>
    /// Streams a local USGS CSV (MRDS flattened or Critical Minerals table) into the store.
    /// Dedupes by ExternalId and by name+state+nearby coords against existing seed rows.
    /// </summary>
    public async Task<ImportResult> ImportFileWithStatsAsync(
        string csvPath,
        LocalityStore store,
        bool unitedStatesOnly = true,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(csvPath);
        if (!File.Exists(csvPath))
            throw new FileNotFoundException("USGS CSV not found.", csvPath);

        await using var stream = new FileStream(
            csvPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 256,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var headerLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("CSV is empty.");
        var header = SplitCsv(headerLine).Select(NormalizeHeader).ToArray();
        var kind = DetectKind(header);
        var seedCandidates = await store.ListSeedDedupeCandidatesAsync(cancellationToken).ConfigureAwait(false);

        var countsByState = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var invalidSamples = new List<InvalidCoordinateRecord>();
        var total = 0;
        var upserted = 0;
        var deduped = 0;
        var skippedNonUs = 0;
        var skippedNameState = 0;
        var invalidCoords = 0;

        await store.BeginBulkWriteAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            const int batchSize = 500;
            const int commitEveryBatches = 10;
            var batchesSinceCommit = 0;
            var batch = new List<Locality>(batchSize);
            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                total++;
                var cols = SplitCsv(line);
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var c = 0; c < header.Length && c < cols.Count; c++)
                {
                    if (!string.IsNullOrWhiteSpace(header[c]))
                        dict[header[c]] = cols[c].Trim();
                }

                var outcome = TryMapRow(dict, kind, unitedStatesOnly);
                if (outcome.SkippedNonUs)
                {
                    skippedNonUs++;
                    continue;
                }

                if (outcome.SkippedMissingNameOrState)
                {
                    skippedNameState++;
                    continue;
                }

                if (outcome.InvalidCoordinate is { } bad)
                {
                    invalidCoords++;
                    if (invalidSamples.Count < 50)
                        invalidSamples.Add(bad);
                    continue;
                }

                if (outcome.Locality is null)
                    continue;

                batch.Add(outcome.Locality);
                if (batch.Count >= batchSize)
                {
                    var (u, d) = await FlushBatchAsync(store, batch, seedCandidates, countsByState, cancellationToken)
                        .ConfigureAwait(false);
                    upserted += u;
                    deduped += d;
                    batch.Clear();
                    batchesSinceCommit++;
                    if (batchesSinceCommit >= commitEveryBatches)
                    {
                        await store.EndBulkWriteAsync(cancellationToken).ConfigureAwait(false);
                        await store.BeginBulkWriteAsync(cancellationToken).ConfigureAwait(false);
                        batchesSinceCommit = 0;
                    }

                    if (total % 25_000 == 0)
                        progress?.Report($"Read {total:N0} rows; upserted {upserted:N0}…");
                }
            }

            if (batch.Count > 0)
            {
                var (u, d) = await FlushBatchAsync(store, batch, seedCandidates, countsByState, cancellationToken)
                    .ConfigureAwait(false);
                upserted += u;
                deduped += d;
            }
        }
        finally
        {
            await store.EndBulkWriteAsync(cancellationToken).ConfigureAwait(false);
        }

        var label = kind == DatasetKind.CriticalMinerals ? CritMinSourceName : MrdsSourceName;
        var vintage = kind == DatasetKind.CriticalMinerals ? CritMinVintage : MrdsVintage;
        progress?.Report(
            $"Done. Upserted {upserted:N0} ({label}, vintage {vintage}); " +
            $"invalid coords logged: {invalidCoords:N0}.");

        return new ImportResult(
            total,
            upserted,
            deduped,
            skippedNonUs,
            skippedNameState,
            invalidCoords,
            countsByState,
            invalidSamples,
            label,
            vintage);
    }

    private static async Task<(int Upserted, int Deduped)> FlushBatchAsync(
        LocalityStore store,
        List<Locality> batch,
        IReadOnlyList<Locality> seedCandidates,
        Dictionary<string, int> countsByState,
        CancellationToken cancellationToken)
    {
        var upserted = 0;
        var deduped = 0;
        foreach (var loc in batch)
        {
            var wasDeduped = await store.UpsertDedupingSeedAsync(loc, seedCandidates, cancellationToken)
                .ConfigureAwait(false);
            upserted++;
            if (wasDeduped)
                deduped++;
            countsByState[loc.StateCode] = countsByState.GetValueOrDefault(loc.StateCode) + 1;
        }

        return (upserted, deduped);
    }

    private enum DatasetKind
    {
        Mrds,
        CriticalMinerals
    }

    private sealed record MapOutcome(
        Locality? Locality,
        InvalidCoordinateRecord? InvalidCoordinate,
        bool SkippedNonUs,
        bool SkippedMissingNameOrState);

    private static DatasetKind DetectKind(IReadOnlyList<string> header)
    {
        var set = header.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (set.Contains("critmin") || set.Contains("lat_wgs84") ||
            (set.Contains("deposit") && !set.Contains("dep_id")))
            return DatasetKind.CriticalMinerals;
        return DatasetKind.Mrds;
    }

    private static MapOutcome TryMapRow(
        Dictionary<string, string> row,
        DatasetKind? detectKind,
        bool unitedStatesOnly)
    {
        var kind = detectKind ?? InferKindFromRow(row);
        return kind == DatasetKind.CriticalMinerals
            ? MapCriticalMineralsRow(row, unitedStatesOnly)
            : MapMrdsRow(row, unitedStatesOnly);
    }

    private static DatasetKind InferKindFromRow(Dictionary<string, string> row)
    {
        if (row.ContainsKey("critmin") || row.ContainsKey("lat_wgs84") ||
            (row.ContainsKey("deposit") && !row.ContainsKey("dep_id")))
            return DatasetKind.CriticalMinerals;
        return DatasetKind.Mrds;
    }

    private static MapOutcome MapMrdsRow(Dictionary<string, string> row, bool unitedStatesOnly)
    {
        var country = First(row, "country", "cntry");
        var stateRaw = First(row, "state", "st", "state_code");

        if (unitedStatesOnly)
        {
            var usCountry = UsStateCodes.IsUnitedStatesCountry(country);
            var usState = UsStateCodes.TryNormalize(stateRaw, out _);
            if (!usCountry && !usState)
                return new MapOutcome(null, null, SkippedNonUs: true, SkippedMissingNameOrState: false);
            if (usCountry && !usState && string.IsNullOrWhiteSpace(stateRaw))
                return new MapOutcome(null, null, SkippedNonUs: true, SkippedMissingNameOrState: false);
        }

        if (!UsStateCodes.TryNormalize(stateRaw, out var stateCode))
        {
            if (unitedStatesOnly)
                return new MapOutcome(null, null, SkippedNonUs: true, SkippedMissingNameOrState: false);
            if (string.IsNullOrWhiteSpace(stateRaw))
                return new MapOutcome(null, null, false, SkippedMissingNameOrState: true);
            stateCode = stateRaw.Trim().ToUpperInvariant();
            if (stateCode.Length > 2)
                stateCode = stateCode[..Math.Min(2, stateCode.Length)];
        }

        var name = First(row, "site_name", "dep_name", "mine_name", "name", "names");
        if (string.IsNullOrWhiteSpace(name))
            return new MapOutcome(null, null, false, SkippedMissingNameOrState: true);

        var latRaw = First(row, "latitude", "lat", "y");
        var lonRaw = First(row, "longitude", "long", "lon", "x");
        if (!TryParseCoordinate(latRaw, lonRaw, out var lat, out var lon, out var coordReason))
        {
            var depForBad = First(row, "dep_id", "mrds_id", "id", "record_id");
            return new MapOutcome(
                null,
                new InvalidCoordinateRecord(
                    string.IsNullOrWhiteSpace(depForBad) ? null : $"usgs-mrds:{depForBad.Trim()}",
                    name.Trim(),
                    stateCode,
                    latRaw,
                    lonRaw,
                    coordReason),
                false,
                false);
        }

        var depId = First(row, "dep_id", "mrds_id", "id", "record_id");
        var externalId = string.IsNullOrWhiteSpace(depId)
            ? $"usgs-mrds:{name.Trim().ToLowerInvariant()}:{stateCode}:{lat:F4}:{lon:F4}"
            : $"usgs-mrds:{depId.Trim()}";

        var commodities = CollectCommodities(row);
        var depType = First(row, "dep_type", "deposit_type");
        // National extract uses dev_stat; older samples used oper_status.
        var operStatus = First(row, "dev_stat", "oper_status", "operating_status", "status");
        var access = MapAccess(operStatus, depType);
        var county = First(row, "county", "county_name");
        var ore = First(row, "ore");
        var hrock = First(row, "hrock_type", "hrock_unit");
        var arock = First(row, "arock_type", "arock_unit");
        var structure = First(row, "structure");
        var tectonic = First(row, "tectonic");
        var reference = First(row, "ref", "url");
        var model = First(row, "model");
        var workType = First(row, "work_type");

        var rating = LocalityRatingCalculator.Create(
            accessibility: access == AccessStatus.Open ? 4 : 3,
            productivity: commodities.Count > 0 ? 6 : 4,
            legalClarity: LocalityRatingCalculator.LegalClarityForUsgsOccurrence,
            recency: LocalityRatingCalculator.RecencyForUsgsMrds,
            beginnerFriendliness: 2,
            variety: Math.Min(10, commodities.Count * 2),
            safety: access is AccessStatus.Restricted or AccessStatus.Closed ? 3 : 5,
            notes: $"{MrdsSourceName} occurrence — low legal-clarity confidence; verify collectability and land status.");

        var sources = new List<string>
        {
            MrdsSourceName,
            $"source_vintage={MrdsVintage}",
            "human-activity-outdated=yes",
            MrdsHumanActivityFlag,
            "https://mrdata.usgs.gov/mrds/"
        };
        if (!string.IsNullOrWhiteSpace(depId))
            sources.Insert(1, $"dep_id={depId.Trim()}");
        if (!string.IsNullOrWhiteSpace(reference))
            sources.Add(reference.Trim());

        var enrichment = BuildMrdsEnrichment(depType, ore, hrock, arock, structure, tectonic, model, workType, reference);

        return new MapOutcome(
            new Locality
            {
                Id = Guid.NewGuid(),
                ExternalId = externalId,
                Name = name.Trim(),
                StateCode = stateCode,
                County = string.IsNullOrWhiteSpace(county) ? null : county,
                Coordinates = new GeoCoordinate(lat, lon),
                ReportedMinerals = commodities,
                LandType = MineOwnershipHints.InferFromText(
                    name,
                    BuildAccessNotes(depType, operStatus, isMrds: true),
                    enrichment),
                AccessStatus = access,
                Difficulty = DifficultyLevel.Intermediate,
                AccessNotes = BuildAccessNotes(depType, operStatus, isMrds: true),
                TypicalFinds = commodities.Count > 0 ? string.Join(", ", commodities) : null,
                HazardNotes = access is AccessStatus.Restricted or AccessStatus.Closed
                    ? "Active or former mine/producer — do not enter without permission. " + MrdsHumanActivityFlag
                    : MrdsHumanActivityFlag,
                Sources = sources,
                EnrichmentSummary = enrichment,
                SystemRating = rating,
                CollectingLimits =
                    "MRDS records describe mineral occurrences, not collecting permissions. " +
                    "LOW legal-clarity tier: dataset does not reflect current land/claim/access status. " +
                    "Verify BLM/USFS/private rules. " + MrdsHumanActivityFlag,
                LastVerifiedUtc = DateTimeOffset.UtcNow,
                SourceDataset = MrdsSourceName,
                SourceVintage = MrdsVintage,
                HumanActivityDataMayBeOutdated = true
            },
            null,
            false,
            false);
    }

    private static MapOutcome MapCriticalMineralsRow(Dictionary<string, string> row, bool unitedStatesOnly)
    {
        // Critical minerals US extract is United States only; still require a valid state.
        var stateRaw = First(row, "state", "st", "state_code");
        if (!UsStateCodes.TryNormalize(stateRaw, out var stateCode))
        {
            if (unitedStatesOnly || string.IsNullOrWhiteSpace(stateRaw))
                return new MapOutcome(null, null, unitedStatesOnly, SkippedMissingNameOrState: !unitedStatesOnly);
            return new MapOutcome(null, null, false, true);
        }

        var name = First(row, "deposit", "site_name", "name");
        if (string.IsNullOrWhiteSpace(name))
            return new MapOutcome(null, null, false, true);

        var latRaw = First(row, "lat_wgs84", "latitude", "lat");
        var lonRaw = First(row, "long_wgs84", "longitude", "lon", "long");
        if (!TryParseCoordinate(latRaw, lonRaw, out var lat, out var lon, out var coordReason))
        {
            return new MapOutcome(
                null,
                new InvalidCoordinateRecord(
                    $"usgs-critmin:{Slug(name)}:{stateCode}",
                    name.Trim(),
                    stateCode,
                    latRaw,
                    lonRaw,
                    coordReason),
                false,
                false);
        }

        var critMin = First(row, "critmin", "commodity", "commod1");
        var depType = First(row, "deptype", "dep_type", "deposit_type");
        var minSystem = First(row, "minsystem", "min_system");
        var focusArea = First(row, "focusarea", "focus_area");
        var depCat = First(row, "depcat", "dep_cat");
        var production = First(row, "production");
        var resources = First(row, "resources");
        var sourceS = First(row, "source_s", "sources");
        var links = First(row, "links", "url");

        var minerals = new List<string>();
        if (!string.IsNullOrWhiteSpace(critMin))
        {
            foreach (var part in critMin.Split([';', ',', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                minerals.Add(part.ToLowerInvariant());
        }

        var access = MapAccess(depCat, depType);
        var externalId = $"usgs-critmin:{Slug(name)}:{stateCode}:{lat:F4}:{lon:F4}";

        var rating = LocalityRatingCalculator.Create(
            accessibility: 3,
            productivity: minerals.Count > 0 ? 7 : 5,
            legalClarity: LocalityRatingCalculator.LegalClarityForUsgsOccurrence,
            recency: LocalityRatingCalculator.RecencyForUsgsCriticalMinerals,
            beginnerFriendliness: 2,
            variety: Math.Min(10, minerals.Count * 2 + 2),
            safety: 4,
            notes: $"{CritMinSourceName} — low legal-clarity confidence; not a collecting permit.");

        var sources = new List<string>
        {
            CritMinSourceName,
            $"source_vintage={CritMinVintage}",
            "human-activity-outdated=yes",
            CritMinHumanActivityFlag,
            "https://mrdata.usgs.gov/uscritmin/",
            "https://doi.org/10.5066/P9K1HBNT"
        };
        if (!string.IsNullOrWhiteSpace(sourceS))
            sources.Add(sourceS.Trim());
        if (!string.IsNullOrWhiteSpace(links))
            sources.Add(links.Trim());

        var enrichmentParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(depType))
            enrichmentParts.Add($"Deposit type: {depType}");
        if (!string.IsNullOrWhiteSpace(minSystem))
            enrichmentParts.Add($"Mineral system: {minSystem}");
        if (!string.IsNullOrWhiteSpace(focusArea))
            enrichmentParts.Add($"Focus area: {focusArea}");
        if (!string.IsNullOrWhiteSpace(depCat))
            enrichmentParts.Add($"Category: {depCat}");
        if (!string.IsNullOrWhiteSpace(production))
            enrichmentParts.Add($"Production: {Truncate(production, 240)}");
        if (!string.IsNullOrWhiteSpace(resources))
            enrichmentParts.Add($"Resources: {Truncate(resources, 240)}");

        return new MapOutcome(
            new Locality
            {
                Id = Guid.NewGuid(),
                ExternalId = externalId,
                Name = name.Trim(),
                StateCode = stateCode,
                Coordinates = new GeoCoordinate(lat, lon),
                ReportedMinerals = minerals,
                LandType = MineOwnershipHints.InferFromText(
                    name,
                    BuildAccessNotes(depType, depCat, isMrds: false),
                    enrichmentParts.Count > 0 ? string.Join(" · ", enrichmentParts) : null),
                AccessStatus = access,
                Difficulty = DifficultyLevel.Advanced,
                AccessNotes = BuildAccessNotes(depType, depCat, isMrds: false),
                TypicalFinds = minerals.Count > 0 ? string.Join(", ", minerals) : null,
                HazardNotes = CritMinHumanActivityFlag,
                Sources = sources,
                EnrichmentSummary = enrichmentParts.Count > 0 ? string.Join(" · ", enrichmentParts) : null,
                SystemRating = rating,
                CollectingLimits =
                    "USGS Critical Minerals locations are deposit inventory records, not collecting sites. " +
                    "LOW legal-clarity tier: verify current land/claim/access status. " + CritMinHumanActivityFlag,
                LastVerifiedUtc = DateTimeOffset.UtcNow,
                SourceDataset = CritMinSourceName,
                SourceVintage = CritMinVintage,
                HumanActivityDataMayBeOutdated = true
            },
            null,
            false,
            false);
    }

    private static string? BuildMrdsEnrichment(
        string? depType,
        string? ore,
        string? hrock,
        string? arock,
        string? structure,
        string? tectonic,
        string? model,
        string? workType,
        string? reference)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(depType))
            parts.Add($"Deposit type: {depType}");
        if (!string.IsNullOrWhiteSpace(ore))
            parts.Add($"Ore: {ore}");
        if (!string.IsNullOrWhiteSpace(hrock))
            parts.Add($"Host rock: {hrock}");
        if (!string.IsNullOrWhiteSpace(arock))
            parts.Add($"Associated rock: {arock}");
        if (!string.IsNullOrWhiteSpace(structure))
            parts.Add($"Structure: {structure}");
        if (!string.IsNullOrWhiteSpace(tectonic))
            parts.Add($"Tectonic: {tectonic}");
        if (!string.IsNullOrWhiteSpace(model))
            parts.Add($"Model: {model}");
        if (!string.IsNullOrWhiteSpace(workType))
            parts.Add($"Workings: {workType}");
        if (!string.IsNullOrWhiteSpace(reference))
            parts.Add($"Ref: {Truncate(reference, 200)}");
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private static bool TryParseCoordinate(
        string latRaw,
        string lonRaw,
        out double lat,
        out double lon,
        out string reason)
    {
        lat = 0;
        lon = 0;
        if (string.IsNullOrWhiteSpace(latRaw) || string.IsNullOrWhiteSpace(lonRaw))
        {
            reason = "missing latitude/longitude";
            return false;
        }

        if (!double.TryParse(latRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out lat) ||
            !double.TryParse(lonRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out lon))
        {
            reason = "unparseable latitude/longitude";
            return false;
        }

        if (lat is < -90 or > 90 || lon is < -180 or > 180)
        {
            reason = "latitude/longitude out of range";
            return false;
        }

        if (Math.Abs(lat) < 1e-9 && Math.Abs(lon) < 1e-9)
        {
            reason = "null-island (0,0) coordinates";
            return false;
        }

        reason = "";
        return true;
    }

    private static AccessStatus MapAccess(string? operStatus, string? depType)
    {
        var s = $"{operStatus} {depType}".ToLowerInvariant();
        if (s.Contains("producer") || s.Contains("operating") || s.Contains("past producer"))
            return AccessStatus.Restricted;
        if (s.Contains("closed") || s.Contains("private"))
            return AccessStatus.Closed;
        if (s.Contains("occurrence") || s.Contains("prospect"))
            return AccessStatus.Unknown;
        return AccessStatus.Unknown;
    }

    private static string? BuildAccessNotes(string? depType, string? operStatus, bool isMrds)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(depType))
            parts.Add($"Type: {depType}");
        if (!string.IsNullOrWhiteSpace(operStatus))
            parts.Add($"Status (dataset): {operStatus}");
        parts.Add(isMrds ? MrdsHumanActivityFlag : CritMinHumanActivityFlag);
        parts.Add("Occurrence context only — not a vetted rockhounding site.");
        return string.Join(" · ", parts);
    }

    private static List<string> CollectCommodities(Dictionary<string, string> row)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in row.Keys)
        {
            if (!key.StartsWith("commod", StringComparison.OrdinalIgnoreCase) &&
                !key.Equals("commodity", StringComparison.OrdinalIgnoreCase) &&
                !key.Equals("commodities", StringComparison.OrdinalIgnoreCase) &&
                !key.Equals("ore", StringComparison.OrdinalIgnoreCase))
                continue;

            // Skip empty ore-only noise when commod* already present — still allow ore minerals.
            foreach (var part in row[key].Split([';', ',', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!string.IsNullOrWhiteSpace(part))
                    set.Add(part.ToLowerInvariant());
            }
        }

        return set.OrderBy(x => x).ToList();
    }

    private static List<Dictionary<string, string>> ParseRows(string csvText)
    {
        var lines = csvText.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2)
            return [];

        var header = SplitCsv(lines[0]).Select(NormalizeHeader).ToArray();
        var rows = new List<Dictionary<string, string>>();

        for (var i = 1; i < lines.Length; i++)
        {
            var cols = SplitCsv(lines[i]);
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var c = 0; c < header.Length && c < cols.Count; c++)
            {
                if (!string.IsNullOrWhiteSpace(header[c]))
                    dict[header[c]] = cols[c].Trim();
            }

            if (dict.Count > 0)
                rows.Add(dict);
        }

        return rows;
    }

    private static string First(Dictionary<string, string> row, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (row.TryGetValue(key, out var val) && !string.IsNullOrWhiteSpace(val))
                return val;
        }

        return "";
    }

    private static string NormalizeHeader(string header) =>
        header.Trim().Trim('"').ToLowerInvariant().Replace(' ', '_');

    private static string Slug(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch))
                sb.Append(ch);
            else if (ch is ' ' or '-' or '_')
                sb.Append('-');
        }

        var s = sb.ToString().Trim('-');
        return s.Length > 64 ? s[..64] : (string.IsNullOrEmpty(s) ? "unnamed" : s);
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";

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
