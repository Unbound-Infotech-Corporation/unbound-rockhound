using System.Globalization;
using System.Text.Json;
using GeoMineralTrace.Core.Geo;
using GeoMineralTrace.Core.Hydrology;
using GeoMineralTrace.Hydrology.Geo;
using Microsoft.Data.Sqlite;

namespace GeoMineralTrace.Hydrology.Storage;

/// <summary>Local-first SQLite store for named U.S. watercourses. Never initiates network I/O.</summary>
public sealed class RiverStore : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string _connectionString;
    private SqliteConnection? _connection;

    public RiverStore(string databasePath)
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
            CREATE TABLE IF NOT EXISTS watercourses (
                id TEXT PRIMARY KEY,
                external_id TEXT,
                name TEXT NOT NULL,
                state_code TEXT NOT NULL,
                county TEXT,
                kind INTEGER NOT NULL DEFAULT 0,
                gnis_id TEXT,
                centroid_lat REAL,
                centroid_lon REAL,
                vertices_json TEXT NOT NULL DEFAULT '[]',
                length_km REAL,
                sources_json TEXT NOT NULL DEFAULT '[]',
                source_dataset TEXT,
                source_vintage TEXT
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ix_watercourses_external_id
                ON watercourses(external_id) WHERE external_id IS NOT NULL;
            CREATE INDEX IF NOT EXISTS ix_watercourses_state ON watercourses(state_code);
            CREATE INDEX IF NOT EXISTS ix_watercourses_name ON watercourses(name);

            CREATE TABLE IF NOT EXISTS river_minerals (
                watercourse_id TEXT NOT NULL,
                mineral_name TEXT NOT NULL,
                association_kind INTEGER NOT NULL,
                confidence INTEGER NOT NULL DEFAULT 1,
                source_locality_id TEXT,
                distance_km REAL,
                citation TEXT,
                notes TEXT,
                PRIMARY KEY (watercourse_id, mineral_name, association_kind)
            );
            CREATE INDEX IF NOT EXISTS ix_river_minerals_name ON river_minerals(mineral_name);
            """;

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

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
        cmd.CommandText = "SELECT COUNT(*) FROM watercourses;";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    public async Task UpsertAsync(Watercourse watercourse, CancellationToken cancellationToken = default)
    {
        EnsureOpen();

        if (!string.IsNullOrWhiteSpace(watercourse.ExternalId))
        {
            var existing = await GetByExternalIdAsync(watercourse.ExternalId, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
                watercourse = CloneWithId(watercourse, existing.Id);
        }

        var centroid = watercourse.Centroid ?? ComputeCentroid(watercourse.Vertices);

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            INSERT INTO watercourses (
                id, external_id, name, state_code, county, kind, gnis_id,
                centroid_lat, centroid_lon, vertices_json, length_km,
                sources_json, source_dataset, source_vintage)
            VALUES (
                $id, $ext, $name, $state, $county, $kind, $gnis,
                $clat, $clon, $verts, $len,
                $sources, $dataset, $vintage)
            ON CONFLICT(id) DO UPDATE SET
                external_id=excluded.external_id,
                name=excluded.name,
                state_code=excluded.state_code,
                county=excluded.county,
                kind=excluded.kind,
                gnis_id=excluded.gnis_id,
                centroid_lat=excluded.centroid_lat,
                centroid_lon=excluded.centroid_lon,
                vertices_json=excluded.vertices_json,
                length_km=excluded.length_km,
                sources_json=excluded.sources_json,
                source_dataset=excluded.source_dataset,
                source_vintage=excluded.source_vintage;
            """;

        cmd.Parameters.AddWithValue("$id", watercourse.Id.ToString("N"));
        cmd.Parameters.AddWithValue("$ext", (object?)watercourse.ExternalId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$name", watercourse.Name);
        cmd.Parameters.AddWithValue("$state", watercourse.StateCode.ToUpperInvariant());
        cmd.Parameters.AddWithValue("$county", (object?)watercourse.County ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$kind", (int)watercourse.Kind);
        cmd.Parameters.AddWithValue("$gnis", (object?)watercourse.GnisId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$clat", centroid?.LatitudeDegrees ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$clon", centroid?.LongitudeDegrees ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$verts", SerializeVertices(watercourse.Vertices));
        cmd.Parameters.AddWithValue("$len", watercourse.LengthKm ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$sources", JsonSerializer.Serialize(watercourse.Sources, JsonOptions));
        cmd.Parameters.AddWithValue("$dataset", (object?)watercourse.SourceDataset ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$vintage", (object?)watercourse.SourceVintage ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpsertMineralAsync(
        RiverMineralAssociation association,
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            INSERT INTO river_minerals (
                watercourse_id, mineral_name, association_kind, confidence,
                source_locality_id, distance_km, citation, notes)
            VALUES ($wid, $mineral, $kind, $conf, $loc, $dist, $cite, $notes)
            ON CONFLICT(watercourse_id, mineral_name, association_kind) DO UPDATE SET
                confidence=excluded.confidence,
                source_locality_id=excluded.source_locality_id,
                distance_km=excluded.distance_km,
                citation=excluded.citation,
                notes=excluded.notes;
            """;
        cmd.Parameters.AddWithValue("$wid", association.WatercourseId.ToString("N"));
        cmd.Parameters.AddWithValue("$mineral", association.MineralName.Trim());
        cmd.Parameters.AddWithValue("$kind", (int)association.AssociationKind);
        cmd.Parameters.AddWithValue("$conf", (int)association.Confidence);
        cmd.Parameters.AddWithValue("$loc", association.SourceLocalityId?.ToString("N") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$dist", association.DistanceKm ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$cite", (object?)association.Citation ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$notes", (object?)association.Notes ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteInferredMineralsAsync(CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "DELETE FROM river_minerals WHERE association_kind = $kind;";
        cmd.Parameters.AddWithValue("$kind", (int)MineralAssociationKind.ProximityInferred);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Watercourse?> GetByExternalIdAsync(string externalId, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT * FROM watercourses WHERE external_id = $ext LIMIT 1;";
        cmd.Parameters.AddWithValue("$ext", externalId);
        var rows = await ReadWatercoursesAsync(cmd, includeMinerals: false, cancellationToken).ConfigureAwait(false);
        return rows.FirstOrDefault();
    }

    public async Task<Watercourse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT * FROM watercourses WHERE id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", id.ToString("N"));
        var rows = await ReadWatercoursesAsync(cmd, includeMinerals: false, cancellationToken).ConfigureAwait(false);
        return rows.FirstOrDefault();
    }

    public async Task<Watercourse?> GetWithMineralsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var wc = await GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (wc is null)
            return null;

        var minerals = await GetMineralsForWatercourseAsync(id, cancellationToken).ConfigureAwait(false);
        return CloneWithMinerals(wc, minerals);
    }

    public async Task<IReadOnlyList<RiverMineralAssociation>> GetMineralsForWatercourseAsync(
        Guid watercourseId,
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            SELECT watercourse_id, mineral_name, association_kind, confidence,
                   source_locality_id, distance_km, citation, notes
            FROM river_minerals WHERE watercourse_id = $id
            ORDER BY association_kind, mineral_name;
            """;
        cmd.Parameters.AddWithValue("$id", watercourseId.ToString("N"));
        return await ReadMineralsAsync(cmd, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Watercourse>> SearchAsync(
        string? stateCode = null,
        string? nameQuery = null,
        string? mineral = null,
        bool curatedOnly = false,
        int limit = 400,
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        var where = new List<string>();
        if (!string.IsNullOrWhiteSpace(stateCode))
        {
            where.Add("w.state_code = $state");
            cmd.Parameters.AddWithValue("$state", stateCode.Trim().ToUpperInvariant());
        }

        if (!string.IsNullOrWhiteSpace(nameQuery))
        {
            where.Add("w.name LIKE $name");
            cmd.Parameters.AddWithValue("$name", "%" + nameQuery.Trim() + "%");
        }

        var join = "";
        if (!string.IsNullOrWhiteSpace(mineral) || curatedOnly)
        {
            join = " INNER JOIN river_minerals rm ON rm.watercourse_id = w.id ";
            if (!string.IsNullOrWhiteSpace(mineral))
            {
                where.Add("rm.mineral_name LIKE $mineral");
                cmd.Parameters.AddWithValue("$mineral", "%" + mineral.Trim() + "%");
            }

            if (curatedOnly)
            {
                where.Add("rm.association_kind = $curated");
                cmd.Parameters.AddWithValue("$curated", (int)MineralAssociationKind.Curated);
            }
        }

        var sql = "SELECT DISTINCT w.* FROM watercourses w" + join;
        if (where.Count > 0)
            sql += " WHERE " + string.Join(" AND ", where);
        sql += " ORDER BY w.name LIMIT $limit;";
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 5000));
        cmd.CommandText = sql;
        return await ReadWatercoursesAsync(cmd, includeMinerals: false, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Watercourse>> FindNearAsync(
        GeoCoordinate center,
        double radiusKm,
        int limit = 80,
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        var latDelta = radiusKm / 111.0;
        var lonDelta = radiusKm / (111.0 * Math.Max(0.2, Math.Cos(center.LatitudeRadians)));

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            SELECT * FROM watercourses
            WHERE centroid_lat IS NOT NULL AND centroid_lon IS NOT NULL
              AND centroid_lat BETWEEN $minLat AND $maxLat
              AND centroid_lon BETWEEN $minLon AND $maxLon;
            """;
        cmd.Parameters.AddWithValue("$minLat", center.LatitudeDegrees - latDelta);
        cmd.Parameters.AddWithValue("$maxLat", center.LatitudeDegrees + latDelta);
        cmd.Parameters.AddWithValue("$minLon", center.LongitudeDegrees - lonDelta);
        cmd.Parameters.AddWithValue("$maxLon", center.LongitudeDegrees + lonDelta);

        var boxed = await ReadWatercoursesAsync(cmd, includeMinerals: false, cancellationToken).ConfigureAwait(false);
        return boxed
            .Where(w => w.Centroid is { } c && PolylineDistance.HaversineKm(center, c) <= radiusKm)
            .OrderBy(w => PolylineDistance.HaversineKm(center, w.Centroid!.Value))
            .Take(limit)
            .ToList();
    }

    public async Task<IReadOnlyList<Watercourse>> ListWithGeometryAsync(
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            SELECT * FROM watercourses
            WHERE centroid_lat IS NOT NULL AND vertices_json != '[]'
            ORDER BY name LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 2000));
        return await ReadWatercoursesAsync(cmd, includeMinerals: false, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Watercourse>> ListAllForEnrichmentAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT * FROM watercourses WHERE centroid_lat IS NOT NULL;";
        return await ReadWatercoursesAsync(cmd, includeMinerals: false, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> HasCuratedMineralAsync(
        Guid watercourseId,
        string mineralName,
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            SELECT 1 FROM river_minerals
            WHERE watercourse_id = $id AND mineral_name = $mineral AND association_kind = $kind
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", watercourseId.ToString("N"));
        cmd.Parameters.AddWithValue("$mineral", mineralName.Trim());
        cmd.Parameters.AddWithValue("$kind", (int)MineralAssociationKind.Curated);
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null;
    }

    public async Task SeedDemoAsync(CancellationToken cancellationToken = default)
    {
        foreach (var wc in DemoWatercourses.Create())
            await UpsertAsync(wc, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }
    }

    private void EnsureOpen()
    {
        if (_connection is null)
            throw new InvalidOperationException("RiverStore not initialized. Call InitializeAsync first.");
    }

    private async Task ExecuteAsync(string sql, CancellationToken cancellationToken)
    {
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<Watercourse>> ReadWatercoursesAsync(
        SqliteCommand cmd,
        bool includeMinerals,
        CancellationToken cancellationToken)
    {
        var list = new List<Watercourse>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var wc = ReadWatercourseRow(reader);
            if (includeMinerals)
            {
                var minerals = await GetMineralsForWatercourseAsync(wc.Id, cancellationToken).ConfigureAwait(false);
                wc = CloneWithMinerals(wc, minerals);
            }

            list.Add(wc);
        }

        return list;
    }

    private static Watercourse ReadWatercourseRow(SqliteDataReader reader)
    {
        var id = Guid.Parse(reader.GetString(reader.GetOrdinal("id")));
        var vertsJson = reader.GetString(reader.GetOrdinal("vertices_json"));
        var sourcesJson = reader.GetString(reader.GetOrdinal("sources_json"));
        GeoCoordinate? centroid = null;
        var clatOrd = reader.GetOrdinal("centroid_lat");
        var clonOrd = reader.GetOrdinal("centroid_lon");
        if (!reader.IsDBNull(clatOrd) && !reader.IsDBNull(clonOrd))
            centroid = new GeoCoordinate(reader.GetDouble(clatOrd), reader.GetDouble(clonOrd));

        return new Watercourse
        {
            Id = id,
            ExternalId = reader.IsDBNull(reader.GetOrdinal("external_id")) ? null : reader.GetString(reader.GetOrdinal("external_id")),
            Name = reader.GetString(reader.GetOrdinal("name")),
            StateCode = reader.GetString(reader.GetOrdinal("state_code")),
            County = reader.IsDBNull(reader.GetOrdinal("county")) ? null : reader.GetString(reader.GetOrdinal("county")),
            Kind = (WatercourseKind)reader.GetInt32(reader.GetOrdinal("kind")),
            GnisId = reader.IsDBNull(reader.GetOrdinal("gnis_id")) ? null : reader.GetString(reader.GetOrdinal("gnis_id")),
            Centroid = centroid,
            Vertices = DeserializeVertices(vertsJson),
            LengthKm = reader.IsDBNull(reader.GetOrdinal("length_km")) ? null : reader.GetDouble(reader.GetOrdinal("length_km")),
            Sources = JsonSerializer.Deserialize<List<string>>(sourcesJson, JsonOptions) ?? [],
            SourceDataset = reader.IsDBNull(reader.GetOrdinal("source_dataset")) ? null : reader.GetString(reader.GetOrdinal("source_dataset")),
            SourceVintage = reader.IsDBNull(reader.GetOrdinal("source_vintage")) ? null : reader.GetString(reader.GetOrdinal("source_vintage"))
        };
    }

    private static async Task<IReadOnlyList<RiverMineralAssociation>> ReadMineralsAsync(
        SqliteCommand cmd,
        CancellationToken cancellationToken)
    {
        var list = new List<RiverMineralAssociation>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            Guid? locId = null;
            if (!reader.IsDBNull(4))
                locId = Guid.Parse(reader.GetString(4));

            list.Add(new RiverMineralAssociation
            {
                WatercourseId = Guid.Parse(reader.GetString(0)),
                MineralName = reader.GetString(1),
                AssociationKind = (MineralAssociationKind)reader.GetInt32(2),
                Confidence = (AssociationConfidence)reader.GetInt32(3),
                SourceLocalityId = locId,
                DistanceKm = reader.IsDBNull(5) ? null : reader.GetDouble(5),
                Citation = reader.IsDBNull(6) ? null : reader.GetString(6),
                Notes = reader.IsDBNull(7) ? null : reader.GetString(7)
            });
        }

        return list;
    }

    private static Watercourse CloneWithId(Watercourse source, Guid id) =>
        new()
        {
            Id = id,
            ExternalId = source.ExternalId,
            Name = source.Name,
            StateCode = source.StateCode,
            County = source.County,
            Kind = source.Kind,
            GnisId = source.GnisId,
            LengthKm = source.LengthKm,
            Vertices = source.Vertices,
            Centroid = source.Centroid,
            Sources = source.Sources,
            SourceDataset = source.SourceDataset,
            SourceVintage = source.SourceVintage,
            Minerals = source.Minerals
        };

    private static Watercourse CloneWithMinerals(Watercourse source, IReadOnlyList<RiverMineralAssociation> minerals) =>
        new()
        {
            Id = source.Id,
            ExternalId = source.ExternalId,
            Name = source.Name,
            StateCode = source.StateCode,
            County = source.County,
            Kind = source.Kind,
            GnisId = source.GnisId,
            LengthKm = source.LengthKm,
            Vertices = source.Vertices,
            Centroid = source.Centroid,
            Sources = source.Sources,
            SourceDataset = source.SourceDataset,
            SourceVintage = source.SourceVintage,
            Minerals = minerals
        };

    public static GeoCoordinate? ComputeCentroid(IReadOnlyList<GeoCoordinate> vertices)
    {
        if (vertices.Count == 0)
            return null;
        var lat = vertices.Average(v => v.LatitudeDegrees);
        var lon = vertices.Average(v => v.LongitudeDegrees);
        return new GeoCoordinate(lat, lon);
    }

    private static string SerializeVertices(IReadOnlyList<GeoCoordinate> vertices) =>
        JsonSerializer.Serialize(
            vertices.Select(v => new[] { v.LatitudeDegrees, v.LongitudeDegrees }),
            JsonOptions);

    private static IReadOnlyList<GeoCoordinate> DeserializeVertices(string json)
    {
        try
        {
            var pairs = JsonSerializer.Deserialize<double[][]>(json, JsonOptions);
            if (pairs is null)
                return [];
            return pairs
                .Where(p => p.Length >= 2)
                .Select(p => new GeoCoordinate(p[0], p[1]))
                .ToList();
        }
        catch
        {
            return [];
        }
    }
}
