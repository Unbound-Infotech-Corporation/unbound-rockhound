using System.Text.Json;
using GeoMineralTrace.Core.Geo;
using GeoMineralTrace.Core.Trails;
using GeoMineralTrace.Hydrology.Geo;
using Microsoft.Data.Sqlite;

namespace GeoMineralTrace.Hydrology.Storage;

/// <summary>Local-first SQLite store for hiking / access trails. Never initiates network I/O.</summary>
public sealed class TrailStore : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string _connectionString;
    private SqliteConnection? _connection;

    public TrailStore(string databasePath)
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
            CREATE TABLE IF NOT EXISTS trails (
                id TEXT PRIMARY KEY,
                external_id TEXT,
                name TEXT NOT NULL,
                state_code TEXT NOT NULL,
                county TEXT,
                kind INTEGER NOT NULL DEFAULT 0,
                centroid_lat REAL,
                centroid_lon REAL,
                vertices_json TEXT NOT NULL DEFAULT '[]',
                length_km REAL,
                sources_json TEXT NOT NULL DEFAULT '[]',
                source_dataset TEXT,
                notes TEXT
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ix_trails_external_id
                ON trails(external_id) WHERE external_id IS NOT NULL;
            CREATE INDEX IF NOT EXISTS ix_trails_state ON trails(state_code);
            CREATE INDEX IF NOT EXISTS ix_trails_name ON trails(name);
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
        cmd.CommandText = "SELECT COUNT(*) FROM trails;";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    public async Task UpsertAsync(Trail trail, CancellationToken cancellationToken = default)
    {
        EnsureOpen();

        if (!string.IsNullOrWhiteSpace(trail.ExternalId))
        {
            var existing = await GetByExternalIdAsync(trail.ExternalId, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
                trail = CloneWithId(trail, existing.Id);
        }

        var centroid = trail.Centroid ?? ComputeCentroid(trail.Vertices);

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            INSERT INTO trails (
                id, external_id, name, state_code, county, kind,
                centroid_lat, centroid_lon, vertices_json, length_km,
                sources_json, source_dataset, notes)
            VALUES (
                $id, $external, $name, $state, $county, $kind,
                $clat, $clon, $verts, $length,
                $sources, $dataset, $notes)
            ON CONFLICT(id) DO UPDATE SET
                external_id = excluded.external_id,
                name = excluded.name,
                state_code = excluded.state_code,
                county = excluded.county,
                kind = excluded.kind,
                centroid_lat = excluded.centroid_lat,
                centroid_lon = excluded.centroid_lon,
                vertices_json = excluded.vertices_json,
                length_km = excluded.length_km,
                sources_json = excluded.sources_json,
                source_dataset = excluded.source_dataset,
                notes = excluded.notes;
            """;
        cmd.Parameters.AddWithValue("$id", trail.Id.ToString("N"));
        cmd.Parameters.AddWithValue("$external", (object?)trail.ExternalId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$name", trail.Name);
        cmd.Parameters.AddWithValue("$state", trail.StateCode);
        cmd.Parameters.AddWithValue("$county", (object?)trail.County ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$kind", (int)trail.Kind);
        cmd.Parameters.AddWithValue("$clat", (object?)centroid?.LatitudeDegrees ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$clon", (object?)centroid?.LongitudeDegrees ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$verts", JsonSerializer.Serialize(
            trail.Vertices.Select(v => new[] { v.LatitudeDegrees, v.LongitudeDegrees }), JsonOptions));
        cmd.Parameters.AddWithValue("$length", (object?)trail.LengthKm ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sources", JsonSerializer.Serialize(trail.Sources, JsonOptions));
        cmd.Parameters.AddWithValue("$dataset", (object?)trail.SourceDataset ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$notes", (object?)trail.Notes ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Trail>> FindNearAsync(
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
            SELECT * FROM trails
            WHERE centroid_lat IS NOT NULL AND centroid_lon IS NOT NULL
              AND centroid_lat BETWEEN $minLat AND $maxLat
              AND centroid_lon BETWEEN $minLon AND $maxLon;
            """;
        cmd.Parameters.AddWithValue("$minLat", center.LatitudeDegrees - latDelta);
        cmd.Parameters.AddWithValue("$maxLat", center.LatitudeDegrees + latDelta);
        cmd.Parameters.AddWithValue("$minLon", center.LongitudeDegrees - lonDelta);
        cmd.Parameters.AddWithValue("$maxLon", center.LongitudeDegrees + lonDelta);

        var boxed = await ReadAllAsync(cmd, cancellationToken).ConfigureAwait(false);
        return boxed
            .Where(t => t.Centroid is { } c && PolylineDistance.HaversineKm(center, c) <= radiusKm)
            .OrderBy(t => PolylineDistance.HaversineKm(center, t.Centroid!.Value))
            .Take(limit)
            .ToList();
    }

    public async Task<IReadOnlyList<Trail>> ListWithGeometryAsync(
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            SELECT * FROM trails
            WHERE centroid_lat IS NOT NULL AND vertices_json != '[]'
            ORDER BY name LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 2000));
        return await ReadAllAsync(cmd, cancellationToken).ConfigureAwait(false);
    }

    public async Task SeedDemoAsync(CancellationToken cancellationToken = default)
    {
        foreach (var trail in DemoTrails.Create())
            await UpsertAsync(trail, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }
    }

    private async Task<Trail?> GetByExternalIdAsync(string externalId, CancellationToken cancellationToken)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT * FROM trails WHERE external_id = $ext LIMIT 1;";
        cmd.Parameters.AddWithValue("$ext", externalId);
        var rows = await ReadAllAsync(cmd, cancellationToken).ConfigureAwait(false);
        return rows.FirstOrDefault();
    }

    private void EnsureOpen()
    {
        if (_connection is null)
            throw new InvalidOperationException("TrailStore not initialized. Call InitializeAsync first.");
    }

    private async Task ExecuteAsync(string sql, CancellationToken cancellationToken)
    {
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<Trail>> ReadAllAsync(SqliteCommand cmd, CancellationToken cancellationToken)
    {
        var list = new List<Trail>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            list.Add(ReadRow(reader));
        return list;
    }

    private static Trail ReadRow(SqliteDataReader reader)
    {
        var id = Guid.Parse(reader.GetString(reader.GetOrdinal("id")));
        var vertsJson = reader.GetString(reader.GetOrdinal("vertices_json"));
        var sourcesJson = reader.GetString(reader.GetOrdinal("sources_json"));
        GeoCoordinate? centroid = null;
        var clatOrd = reader.GetOrdinal("centroid_lat");
        var clonOrd = reader.GetOrdinal("centroid_lon");
        if (!reader.IsDBNull(clatOrd) && !reader.IsDBNull(clonOrd))
            centroid = new GeoCoordinate(reader.GetDouble(clatOrd), reader.GetDouble(clonOrd));

        var verts = JsonSerializer.Deserialize<List<double[]>>(vertsJson, JsonOptions) ?? [];
        var sources = JsonSerializer.Deserialize<List<string>>(sourcesJson, JsonOptions) ?? [];

        return new Trail
        {
            Id = id,
            ExternalId = reader.IsDBNull(reader.GetOrdinal("external_id"))
                ? null
                : reader.GetString(reader.GetOrdinal("external_id")),
            Name = reader.GetString(reader.GetOrdinal("name")),
            StateCode = reader.GetString(reader.GetOrdinal("state_code")),
            County = reader.IsDBNull(reader.GetOrdinal("county"))
                ? null
                : reader.GetString(reader.GetOrdinal("county")),
            Kind = (TrailKind)reader.GetInt32(reader.GetOrdinal("kind")),
            LengthKm = reader.IsDBNull(reader.GetOrdinal("length_km"))
                ? null
                : reader.GetDouble(reader.GetOrdinal("length_km")),
            Vertices = verts
                .Where(v => v.Length >= 2)
                .Select(v => new GeoCoordinate(v[0], v[1]))
                .ToList(),
            Centroid = centroid,
            Sources = sources,
            SourceDataset = reader.IsDBNull(reader.GetOrdinal("source_dataset"))
                ? null
                : reader.GetString(reader.GetOrdinal("source_dataset")),
            Notes = reader.IsDBNull(reader.GetOrdinal("notes"))
                ? null
                : reader.GetString(reader.GetOrdinal("notes"))
        };
    }

    private static Trail CloneWithId(Trail trail, Guid id) => new()
    {
        Id = id,
        ExternalId = trail.ExternalId,
        Name = trail.Name,
        StateCode = trail.StateCode,
        County = trail.County,
        Kind = trail.Kind,
        LengthKm = trail.LengthKm,
        Vertices = trail.Vertices,
        Centroid = trail.Centroid,
        Sources = trail.Sources,
        SourceDataset = trail.SourceDataset,
        Notes = trail.Notes
    };

    public static GeoCoordinate? ComputeCentroid(IReadOnlyList<GeoCoordinate> vertices)
    {
        if (vertices.Count == 0)
            return null;
        return new GeoCoordinate(
            vertices.Average(v => v.LatitudeDegrees),
            vertices.Average(v => v.LongitudeDegrees));
    }
}
