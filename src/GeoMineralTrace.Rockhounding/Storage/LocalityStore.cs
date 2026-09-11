using GeoMineralTrace.Core.Geo;
using GeoMineralTrace.Core.Rockhounding;
using Microsoft.Data.Sqlite;

namespace GeoMineralTrace.Rockhounding.Storage;

/// <summary>
/// Local-first SQLite store for U.S. rockhounding localities.
/// Online enrichment must be explicitly opted in by the caller — this type
/// never initiates network I/O.
/// </summary>
public sealed class LocalityStore : IAsyncDisposable
{
    private readonly string _connectionString;
    private SqliteConnection? _connection;

    public LocalityStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _connection = new SqliteConnection(_connectionString);
        await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        const string sql = """
            CREATE TABLE IF NOT EXISTS localities (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                state_code TEXT NOT NULL,
                county TEXT,
                latitude REAL,
                longitude REAL,
                altitude_m REAL,
                minerals_json TEXT NOT NULL DEFAULT '[]',
                land_type INTEGER NOT NULL DEFAULT 0,
                access_notes TEXT,
                typical_finds TEXT,
                difficulty INTEGER NOT NULL DEFAULT 0,
                seasonality TEXT,
                collecting_limits TEXT,
                access_status INTEGER NOT NULL DEFAULT 0,
                hazard_notes TEXT,
                nearest_services TEXT,
                sources_json TEXT NOT NULL DEFAULT '[]',
                enrichment_summary TEXT,
                rating_json TEXT,
                user_avg_rating REAL,
                user_rating_count INTEGER NOT NULL DEFAULT 0,
                last_verified_utc TEXT
            );
            CREATE INDEX IF NOT EXISTS ix_localities_state ON localities(state_code);
            CREATE INDEX IF NOT EXISTS ix_localities_access ON localities(access_status);
            """;

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        await EnsureExternalIdColumnAsync(cancellationToken).ConfigureAwait(false);
        await EnsureSourceMetadataColumnsAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureExternalIdColumnAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ExecuteAsync(
                "ALTER TABLE localities ADD COLUMN external_id TEXT;",
                cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException)
        {
            // Column already exists.
        }

        await ExecuteAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS ix_localities_external_id ON localities(external_id) WHERE external_id IS NOT NULL;",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureSourceMetadataColumnsAsync(CancellationToken cancellationToken)
    {
        foreach (var sql in new[]
                 {
                     "ALTER TABLE localities ADD COLUMN source_dataset TEXT;",
                     "ALTER TABLE localities ADD COLUMN source_vintage TEXT;",
                     "ALTER TABLE localities ADD COLUMN human_activity_outdated INTEGER NOT NULL DEFAULT 0;"
                 })
        {
            try
            {
                await ExecuteAsync(sql, cancellationToken).ConfigureAwait(false);
            }
            catch (SqliteException)
            {
                // Column already exists.
            }
        }
    }

    /// <summary>Speeds national bulk imports (WAL + deferred sync). Pair with <see cref="EndBulkWriteAsync"/>.</summary>
    public async Task BeginBulkWriteAsync(CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await ExecuteAsync("PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync("PRAGMA synchronous=NORMAL;", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync("BEGIN TRANSACTION;", cancellationToken).ConfigureAwait(false);
    }

    public async Task EndBulkWriteAsync(CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        try
        {
            await ExecuteAsync("COMMIT;", cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException)
        {
            await ExecuteAsync("ROLLBACK;", cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM localities;";
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Rows eligible for name/state/coord seed dedupe (non-USGS or missing external id).
    /// Call once before a bulk USGS import — typically a small set.
    /// </summary>
    public async Task<IReadOnlyList<Locality>> ListSeedDedupeCandidatesAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            SELECT * FROM localities
            WHERE external_id IS NULL
               OR (external_id NOT LIKE 'usgs-mrds:%' AND external_id NOT LIKE 'usgs-critmin:%');
            """;
        return await ReadAllAsync(cmd, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Upserts by ExternalId; optionally merges onto a provided seed candidate list
    /// (name+state within ~0.5 km). Returns true when an existing row was reused.
    /// </summary>
    public async Task<bool> UpsertDedupingSeedAsync(
        Locality locality,
        IReadOnlyList<Locality>? seedCandidates = null,
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        var reused = false;

        if (!string.IsNullOrWhiteSpace(locality.ExternalId))
        {
            var byExt = await GetByExternalIdAsync(locality.ExternalId, cancellationToken).ConfigureAwait(false);
            if (byExt is not null)
            {
                locality = ClonePreservingIdentity(locality, byExt);
                reused = true;
            }
        }

        if (!reused && locality.Coordinates is { } coords && seedCandidates is { Count: > 0 })
        {
            var seed = FindSeedDuplicateInMemory(
                seedCandidates,
                locality.Name,
                locality.StateCode,
                coords,
                maxDistanceKm: 0.5);
            if (seed is not null)
            {
                var externalId = locality.ExternalId ?? seed.ExternalId;
                var mergedSources = seed.Sources
                    .Concat(locality.Sources)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var mergedMinerals = seed.ReportedMinerals
                    .Concat(locality.ReportedMinerals)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x)
                    .ToList();

                locality = new Locality
                {
                    Id = seed.Id,
                    ExternalId = externalId,
                    Name = locality.Name,
                    StateCode = locality.StateCode,
                    County = locality.County ?? seed.County,
                    Coordinates = locality.Coordinates ?? seed.Coordinates,
                    ReportedMinerals = mergedMinerals.Count > 0 ? mergedMinerals : locality.ReportedMinerals,
                    LandType = seed.LandType != LandType.Unknown ? seed.LandType : locality.LandType,
                    AccessNotes = locality.AccessNotes ?? seed.AccessNotes,
                    TypicalFinds = locality.TypicalFinds ?? seed.TypicalFinds,
                    Difficulty = seed.Difficulty != DifficultyLevel.Unknown ? seed.Difficulty : locality.Difficulty,
                    SeasonalityNotes = seed.SeasonalityNotes ?? locality.SeasonalityNotes,
                    CollectingLimits = locality.CollectingLimits ?? seed.CollectingLimits,
                    AccessStatus = locality.AccessStatus,
                    HazardNotes = locality.HazardNotes ?? seed.HazardNotes,
                    NearestServices = seed.NearestServices ?? locality.NearestServices,
                    Sources = mergedSources,
                    EnrichmentSummary = locality.EnrichmentSummary ?? seed.EnrichmentSummary,
                    SystemRating = locality.SystemRating ?? seed.SystemRating,
                    UserAverageRating = seed.UserAverageRating,
                    UserRatingCount = seed.UserRatingCount,
                    LastVerifiedUtc = locality.LastVerifiedUtc ?? seed.LastVerifiedUtc,
                    SourceDataset = locality.SourceDataset ?? seed.SourceDataset,
                    SourceVintage = locality.SourceVintage ?? seed.SourceVintage,
                    HumanActivityDataMayBeOutdated =
                        locality.HumanActivityDataMayBeOutdated || seed.HumanActivityDataMayBeOutdated
                };
                reused = true;
            }
        }

        await UpsertAsync(locality, cancellationToken, resolveExternalId: false).ConfigureAwait(false);
        return reused;
    }

    private static Locality? FindSeedDuplicateInMemory(
        IReadOnlyList<Locality> candidates,
        string name,
        string stateCode,
        GeoCoordinate coords,
        double maxDistanceKm)
    {
        var nameKey = NormalizeName(name);
        return candidates
            .Select(c => (Loc: c, Coords: c.Coordinates))
            .Where(t =>
                t.Coords is { } cc &&
                string.Equals(t.Loc.StateCode, stateCode, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(NormalizeName(t.Loc.Name), nameKey, StringComparison.Ordinal) &&
                HaversineKm(coords, cc) <= maxDistanceKm)
            .OrderBy(t => HaversineKm(coords, t.Coords!.Value))
            .Select(t => t.Loc)
            .FirstOrDefault();
    }

    private static string NormalizeName(string name) =>
        string.Join(' ', name.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static Locality ClonePreservingIdentity(Locality incoming, Locality existing) =>
        new()
        {
            Id = existing.Id,
            ExternalId = incoming.ExternalId ?? existing.ExternalId,
            Name = incoming.Name,
            StateCode = incoming.StateCode,
            County = incoming.County,
            Coordinates = incoming.Coordinates,
            ReportedMinerals = incoming.ReportedMinerals,
            LandType = incoming.LandType,
            AccessNotes = incoming.AccessNotes,
            TypicalFinds = incoming.TypicalFinds,
            Difficulty = incoming.Difficulty,
            SeasonalityNotes = incoming.SeasonalityNotes,
            CollectingLimits = incoming.CollectingLimits,
            AccessStatus = incoming.AccessStatus,
            HazardNotes = incoming.HazardNotes,
            NearestServices = incoming.NearestServices,
            Sources = incoming.Sources,
            EnrichmentSummary = incoming.EnrichmentSummary,
            SystemRating = incoming.SystemRating,
            UserAverageRating = existing.UserAverageRating,
            UserRatingCount = existing.UserRatingCount,
            LastVerifiedUtc = incoming.LastVerifiedUtc,
            SourceDataset = incoming.SourceDataset,
            SourceVintage = incoming.SourceVintage,
            HumanActivityDataMayBeOutdated = incoming.HumanActivityDataMayBeOutdated
        };

    public async Task<Locality?> GetByExternalIdAsync(string externalId, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT * FROM localities WHERE external_id = $ext LIMIT 1;";
        cmd.Parameters.AddWithValue("$ext", externalId);
        var rows = await ReadAllAsync(cmd, cancellationToken).ConfigureAwait(false);
        return rows.FirstOrDefault();
    }

    public async Task UpsertAsync(
        Locality locality,
        CancellationToken cancellationToken = default,
        bool resolveExternalId = true)
    {
        EnsureOpen();

        if (resolveExternalId && !string.IsNullOrWhiteSpace(locality.ExternalId))
        {
            var existing = await GetByExternalIdAsync(locality.ExternalId, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
                locality = ClonePreservingIdentity(locality, existing);
        }

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            INSERT INTO localities (
                id, external_id, name, state_code, county, latitude, longitude, altitude_m,
                minerals_json, land_type, access_notes, typical_finds, difficulty,
                seasonality, collecting_limits, access_status, hazard_notes,
                nearest_services, sources_json, enrichment_summary, rating_json,
                user_avg_rating, user_rating_count, last_verified_utc,
                source_dataset, source_vintage, human_activity_outdated)
            VALUES (
                $id, $externalId, $name, $state, $county, $lat, $lon, $alt,
                $minerals, $land, $accessNotes, $finds, $diff,
                $season, $limits, $access, $hazards,
                $services, $sources, $enrichment, $rating,
                $userAvg, $userCount, $verified,
                $sourceDataset, $sourceVintage, $humanOutdated)
            ON CONFLICT(id) DO UPDATE SET
                external_id=excluded.external_id,
                name=excluded.name,
                state_code=excluded.state_code,
                county=excluded.county,
                latitude=excluded.latitude,
                longitude=excluded.longitude,
                altitude_m=excluded.altitude_m,
                minerals_json=excluded.minerals_json,
                land_type=excluded.land_type,
                access_notes=excluded.access_notes,
                typical_finds=excluded.typical_finds,
                difficulty=excluded.difficulty,
                seasonality=excluded.seasonality,
                collecting_limits=excluded.collecting_limits,
                access_status=excluded.access_status,
                hazard_notes=excluded.hazard_notes,
                nearest_services=excluded.nearest_services,
                sources_json=excluded.sources_json,
                enrichment_summary=excluded.enrichment_summary,
                rating_json=excluded.rating_json,
                user_avg_rating=excluded.user_avg_rating,
                user_rating_count=excluded.user_rating_count,
                last_verified_utc=excluded.last_verified_utc,
                source_dataset=excluded.source_dataset,
                source_vintage=excluded.source_vintage,
                human_activity_outdated=excluded.human_activity_outdated;
            """;

        AddLocalityParameters(cmd, locality);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Locality>> ListByStateAsync(
        string stateCode,
        int limit = 500,
        int offset = 0,
        LandType? landType = null,
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        var landClause = landType is { } lt ? " AND land_type = $land" : "";
        cmd.CommandText = $"""
            SELECT * FROM localities
            WHERE state_code = $state{landClause}
            ORDER BY name
            LIMIT $limit OFFSET $offset;
            """;
        cmd.Parameters.AddWithValue("$state", stateCode.ToUpperInvariant());
        if (landType is { } land)
            cmd.Parameters.AddWithValue("$land", (int)land);
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 5000));
        cmd.Parameters.AddWithValue("$offset", Math.Max(0, offset));
        return await ReadAllAsync(cmd, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> CountByStateAsync(
        string stateCode,
        LandType? landType = null,
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        var landClause = landType is { } ? " AND land_type = $land" : "";
        cmd.CommandText = $"SELECT COUNT(*) FROM localities WHERE state_code = $state{landClause};";
        cmd.Parameters.AddWithValue("$state", stateCode.ToUpperInvariant());
        if (landType is { } land)
            cmd.Parameters.AddWithValue("$land", (int)land);
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task<IReadOnlyList<Locality>> ListPublicAsync(
        string? stateCode = null,
        LandType? publicLandType = null,
        string? mineral = null,
        int limit = 100,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        var where = new List<string> { "land_type IN (1, 2, 3, 4)" }; // PublicBlm…StateLand
        if (!string.IsNullOrWhiteSpace(stateCode))
        {
            where.Add("state_code = $state");
            cmd.Parameters.AddWithValue("$state", stateCode.Trim().ToUpperInvariant());
        }

        if (publicLandType is { } lt && lt is LandType.PublicBlm or LandType.PublicUsfs or LandType.StatePark or LandType.StateLand)
        {
            where.Add("land_type = $land");
            cmd.Parameters.AddWithValue("$land", (int)lt);
        }

        if (!string.IsNullOrWhiteSpace(mineral))
        {
            where.Add("minerals_json LIKE $mineral");
            cmd.Parameters.AddWithValue("$mineral", "%" + mineral.Trim().ToLowerInvariant() + "%");
        }

        cmd.CommandText = $"""
            SELECT * FROM localities
            WHERE {string.Join(" AND ", where)}
            ORDER BY state_code, name
            LIMIT $limit OFFSET $offset;
            """;
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 5000));
        cmd.Parameters.AddWithValue("$offset", Math.Max(0, offset));
        return await ReadAllAsync(cmd, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> CountPublicAsync(
        string? stateCode = null,
        LandType? publicLandType = null,
        string? mineral = null,
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        var where = new List<string> { "land_type IN (1, 2, 3, 4)" };
        if (!string.IsNullOrWhiteSpace(stateCode))
        {
            where.Add("state_code = $state");
            cmd.Parameters.AddWithValue("$state", stateCode.Trim().ToUpperInvariant());
        }

        if (publicLandType is { } lt && lt is LandType.PublicBlm or LandType.PublicUsfs or LandType.StatePark or LandType.StateLand)
        {
            where.Add("land_type = $land");
            cmd.Parameters.AddWithValue("$land", (int)lt);
        }

        if (!string.IsNullOrWhiteSpace(mineral))
        {
            where.Add("minerals_json LIKE $mineral");
            cmd.Parameters.AddWithValue("$mineral", "%" + mineral.Trim().ToLowerInvariant() + "%");
        }

        cmd.CommandText = $"SELECT COUNT(*) FROM localities WHERE {string.Join(" AND ", where)};";
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task<IReadOnlyDictionary<LandType, int>> CountByLandTypeAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT land_type, COUNT(*) FROM localities GROUP BY land_type;";
        var dict = new Dictionary<LandType, int>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            dict[(LandType)reader.GetInt32(0)] = reader.GetInt32(1);
        return dict;
    }

    public async Task<int> PatchLandTypeAsync(
        Guid id,
        LandType landType,
        string? accessNotesAppend = null,
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        if (string.IsNullOrWhiteSpace(accessNotesAppend))
        {
            cmd.CommandText = "UPDATE localities SET land_type = $land WHERE id = $id;";
        }
        else
        {
            cmd.CommandText = """
                UPDATE localities
                SET land_type = $land,
                    access_notes = CASE
                        WHEN access_notes IS NULL OR access_notes = '' THEN $notes
                        WHEN instr(access_notes, $notes) > 0 THEN access_notes
                        ELSE access_notes || ' · ' || $notes
                    END
                WHERE id = $id;
                """;
            cmd.Parameters.AddWithValue("$notes", accessNotesAppend.Trim());
        }

        cmd.Parameters.AddWithValue("$land", (int)landType);
        cmd.Parameters.AddWithValue("$id", id.ToString("N"));
        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lightweight scan for ownership enrichment (id + coords + current land + text fields).</summary>
    public async Task<IReadOnlyList<LocalityOwnershipRow>> ListOwnershipScanRowsAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, latitude, longitude, land_type, access_notes, enrichment_summary
            FROM localities
            WHERE latitude IS NOT NULL AND longitude IS NOT NULL;
            """;
        var list = new List<LocalityOwnershipRow>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new LocalityOwnershipRow(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetDouble(2),
                reader.GetDouble(3),
                (LandType)reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return list;
    }

    public async Task<IReadOnlyList<Locality>> FindNearAsync(
        GeoCoordinate center,
        double radiusKm,
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        // Bounding-box prefilter then Haversine refine (offline, no spatial extension required).
        var latDelta = radiusKm / 111.0;
        var lonDelta = radiusKm / (111.0 * Math.Max(0.2, Math.Cos(center.LatitudeRadians)));

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            SELECT * FROM localities
            WHERE latitude IS NOT NULL AND longitude IS NOT NULL
              AND latitude BETWEEN $minLat AND $maxLat
              AND longitude BETWEEN $minLon AND $maxLon;
            """;
        cmd.Parameters.AddWithValue("$minLat", center.LatitudeDegrees - latDelta);
        cmd.Parameters.AddWithValue("$maxLat", center.LatitudeDegrees + latDelta);
        cmd.Parameters.AddWithValue("$minLon", center.LongitudeDegrees - lonDelta);
        cmd.Parameters.AddWithValue("$maxLon", center.LongitudeDegrees + lonDelta);

        var boxed = await ReadAllAsync(cmd, cancellationToken).ConfigureAwait(false);
        return boxed
            .Where(l => l.Coordinates is { } c && HaversineKm(center, c) <= radiusKm)
            .OrderBy(l => HaversineKm(center, l.Coordinates!.Value))
            .Take(2000)
            .ToList();
    }

    public async Task<IReadOnlyList<Locality>> SearchByMineralAsync(
        string mineralName,
        int limit = 500,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            SELECT * FROM localities
            WHERE minerals_json LIKE $q
            ORDER BY name
            LIMIT $limit OFFSET $offset;
            """;
        cmd.Parameters.AddWithValue("$q", $"%{mineralName}%");
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 5000));
        cmd.Parameters.AddWithValue("$offset", Math.Max(0, offset));
        return await ReadAllAsync(cmd, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> CountByMineralAsync(
        string mineralName,
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM localities WHERE minerals_json LIKE $q;";
        cmd.Parameters.AddWithValue("$q", $"%{mineralName}%");
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Distinct mineral/commodity names across all localities (for pickers).</summary>
    public async Task<IReadOnlyList<string>> ListDistinctMineralsAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT minerals_json FROM localities WHERE minerals_json IS NOT NULL AND minerals_json <> '' AND minerals_json <> '[]';";
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (reader.IsDBNull(0)) continue;
            var json = reader.GetString(0);
            try
            {
                var list = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? [];
                foreach (var m in list)
                {
                    if (!string.IsNullOrWhiteSpace(m))
                        set.Add(m.Trim());
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // ignore malformed rows
            }
        }

        return set.OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Distinct mineral/commodity names reported for localities in a state.</summary>
    public async Task<IReadOnlyList<string>> ListDistinctMineralsForStateAsync(
        string stateCode,
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT minerals_json FROM localities WHERE state_code = $state;";
        cmd.Parameters.AddWithValue("$state", stateCode.Trim().ToUpperInvariant());
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (reader.IsDBNull(0)) continue;
            var json = reader.GetString(0);
            try
            {
                var list = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? [];
                foreach (var m in list)
                {
                    if (!string.IsNullOrWhiteSpace(m))
                        set.Add(m.Trim());
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // ignore malformed rows
            }
        }

        return set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task SeedDemoDataAsync(CancellationToken cancellationToken = default)
    {
        var demos = DemoLocalities.Create();
        foreach (var locality in demos)
            await UpsertAsync(locality, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Locality>> ListWithCoordinatesAsync(
        int limit = 500,
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            SELECT * FROM localities
            WHERE latitude IS NOT NULL AND longitude IS NOT NULL
            ORDER BY name
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 5000));
        return await ReadAllAsync(cmd, cancellationToken).ConfigureAwait(false);
    }

    public async Task ExecuteAsync(
        string sql,
        CancellationToken cancellationToken = default,
        Action<SqliteCommand>? bind = null)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        bind?.Invoke(cmd);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Locality?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT * FROM localities WHERE id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", id.ToString("N"));
        var rows = await ReadAllAsync(cmd, cancellationToken).ConfigureAwait(false);
        return rows.FirstOrDefault();
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
            SqliteConnection.ClearAllPools();
        }
    }

    private void EnsureOpen()
    {
        if (_connection is null)
            throw new InvalidOperationException("Call InitializeAsync before using LocalityStore.");
    }

    private static void AddLocalityParameters(SqliteCommand cmd, Locality locality)
    {
        cmd.Parameters.AddWithValue("$id", locality.Id.ToString("N"));
        cmd.Parameters.AddWithValue("$externalId", (object?)locality.ExternalId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$name", locality.Name);
        cmd.Parameters.AddWithValue("$state", locality.StateCode);
        cmd.Parameters.AddWithValue("$county", (object?)locality.County ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$lat", (object?)locality.Coordinates?.LatitudeDegrees ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$lon", (object?)locality.Coordinates?.LongitudeDegrees ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$alt", (object?)locality.Coordinates?.AltitudeMeters ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$minerals", System.Text.Json.JsonSerializer.Serialize(locality.ReportedMinerals));
        cmd.Parameters.AddWithValue("$land", (int)locality.LandType);
        cmd.Parameters.AddWithValue("$accessNotes", (object?)locality.AccessNotes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$finds", (object?)locality.TypicalFinds ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$diff", (int)locality.Difficulty);
        cmd.Parameters.AddWithValue("$season", (object?)locality.SeasonalityNotes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$limits", (object?)locality.CollectingLimits ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$access", (int)locality.AccessStatus);
        cmd.Parameters.AddWithValue("$hazards", (object?)locality.HazardNotes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$services", (object?)locality.NearestServices ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sources", System.Text.Json.JsonSerializer.Serialize(locality.Sources));
        cmd.Parameters.AddWithValue("$enrichment", (object?)locality.EnrichmentSummary ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$rating",
            locality.SystemRating is null
                ? DBNull.Value
                : System.Text.Json.JsonSerializer.Serialize(locality.SystemRating));
        cmd.Parameters.AddWithValue("$userAvg", (object?)locality.UserAverageRating ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$userCount", locality.UserRatingCount);
        cmd.Parameters.AddWithValue("$verified",
            (object?)locality.LastVerifiedUtc?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sourceDataset", (object?)locality.SourceDataset ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sourceVintage", (object?)locality.SourceVintage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$humanOutdated", locality.HumanActivityDataMayBeOutdated ? 1 : 0);
    }

    private static async Task<IReadOnlyList<Locality>> ReadAllAsync(
        SqliteCommand cmd,
        CancellationToken cancellationToken)
    {
        var list = new List<Locality>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            list.Add(Map(reader));
        return list;
    }

    private static Locality Map(SqliteDataReader reader)
    {
        double? lat = reader.IsDBNull(reader.GetOrdinal("latitude")) ? null : reader.GetDouble(reader.GetOrdinal("latitude"));
        double? lon = reader.IsDBNull(reader.GetOrdinal("longitude")) ? null : reader.GetDouble(reader.GetOrdinal("longitude"));
        double? alt = reader.IsDBNull(reader.GetOrdinal("altitude_m")) ? null : reader.GetDouble(reader.GetOrdinal("altitude_m"));

        GeoCoordinate? coords = lat is { } la && lon is { } lo
            ? new GeoCoordinate(la, lo, alt)
            : null;

        var minerals = System.Text.Json.JsonSerializer.Deserialize<List<string>>(
            reader.GetString(reader.GetOrdinal("minerals_json"))) ?? [];
        var sources = System.Text.Json.JsonSerializer.Deserialize<List<string>>(
            reader.GetString(reader.GetOrdinal("sources_json"))) ?? [];

        LocalityRating? rating = null;
        if (!reader.IsDBNull(reader.GetOrdinal("rating_json")))
        {
            rating = System.Text.Json.JsonSerializer.Deserialize<LocalityRating>(
                reader.GetString(reader.GetOrdinal("rating_json")));
        }

        return new Locality
        {
            Id = Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
            ExternalId = TryGetString(reader, "external_id"),
            Name = reader.GetString(reader.GetOrdinal("name")),
            StateCode = reader.GetString(reader.GetOrdinal("state_code")),
            County = reader.IsDBNull(reader.GetOrdinal("county")) ? null : reader.GetString(reader.GetOrdinal("county")),
            Coordinates = coords,
            ReportedMinerals = minerals,
            LandType = (LandType)reader.GetInt32(reader.GetOrdinal("land_type")),
            AccessNotes = reader.IsDBNull(reader.GetOrdinal("access_notes")) ? null : reader.GetString(reader.GetOrdinal("access_notes")),
            TypicalFinds = reader.IsDBNull(reader.GetOrdinal("typical_finds")) ? null : reader.GetString(reader.GetOrdinal("typical_finds")),
            Difficulty = (DifficultyLevel)reader.GetInt32(reader.GetOrdinal("difficulty")),
            SeasonalityNotes = reader.IsDBNull(reader.GetOrdinal("seasonality")) ? null : reader.GetString(reader.GetOrdinal("seasonality")),
            CollectingLimits = reader.IsDBNull(reader.GetOrdinal("collecting_limits")) ? null : reader.GetString(reader.GetOrdinal("collecting_limits")),
            AccessStatus = (AccessStatus)reader.GetInt32(reader.GetOrdinal("access_status")),
            HazardNotes = reader.IsDBNull(reader.GetOrdinal("hazard_notes")) ? null : reader.GetString(reader.GetOrdinal("hazard_notes")),
            NearestServices = reader.IsDBNull(reader.GetOrdinal("nearest_services")) ? null : reader.GetString(reader.GetOrdinal("nearest_services")),
            Sources = sources,
            EnrichmentSummary = reader.IsDBNull(reader.GetOrdinal("enrichment_summary")) ? null : reader.GetString(reader.GetOrdinal("enrichment_summary")),
            SystemRating = rating,
            UserAverageRating = reader.IsDBNull(reader.GetOrdinal("user_avg_rating")) ? null : reader.GetDouble(reader.GetOrdinal("user_avg_rating")),
            UserRatingCount = reader.GetInt32(reader.GetOrdinal("user_rating_count")),
            LastVerifiedUtc = reader.IsDBNull(reader.GetOrdinal("last_verified_utc"))
                ? null
                : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("last_verified_utc"))),
            SourceDataset = TryGetString(reader, "source_dataset"),
            SourceVintage = TryGetString(reader, "source_vintage"),
            HumanActivityDataMayBeOutdated = TryGetInt(reader, "human_activity_outdated") == 1
        };
    }

    private static int TryGetInt(SqliteDataReader reader, string column)
    {
        try
        {
            var ord = reader.GetOrdinal(column);
            return reader.IsDBNull(ord) ? 0 : reader.GetInt32(ord);
        }
        catch (IndexOutOfRangeException)
        {
            return 0;
        }
    }

    public static double HaversineKm(GeoCoordinate a, GeoCoordinate b)
    {
        const double r = 6371.0;
        var dLat = GeoCoordinate.DegreesToRadians(b.LatitudeDegrees - a.LatitudeDegrees);
        var dLon = GeoCoordinate.DegreesToRadians(b.LongitudeDegrees - a.LongitudeDegrees);
        var lat1 = a.LatitudeRadians;
        var lat2 = b.LatitudeRadians;
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * r * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    private static string? TryGetString(SqliteDataReader reader, string column)
    {
        try
        {
            var ord = reader.GetOrdinal(column);
            return reader.IsDBNull(ord) ? null : reader.GetString(ord);
        }
        catch (IndexOutOfRangeException)
        {
            return null;
        }
    }
}

/// <summary>Minimal locality fields for batch ownership enrichment.</summary>
public readonly record struct LocalityOwnershipRow(
    Guid Id,
    string Name,
    double Latitude,
    double Longitude,
    LandType LandType,
    string? AccessNotes,
    string? EnrichmentSummary);
