using GeoMineralTrace.Core.Geo;
using GeoMineralTrace.Core.Rockhounding;
using Microsoft.Data.Sqlite;

namespace GeoMineralTrace.Rockhounding.Storage;

/// <summary>Offline SQLite store for rumoured / unverified collecting leads.</summary>
public sealed class RumouredSiteStore : IAsyncDisposable
{
    private readonly string _connectionString;
    private SqliteConnection? _connection;

    public RumouredSiteStore(string databasePath)
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
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS rumoured_sites (
                id TEXT PRIMARY KEY,
                external_id TEXT,
                name TEXT NOT NULL,
                state_code TEXT NOT NULL,
                latitude REAL,
                longitude REAL,
                minerals_json TEXT NOT NULL DEFAULT '[]',
                source_type INTEGER NOT NULL DEFAULT 0,
                source_url TEXT,
                source_label TEXT,
                notes TEXT,
                confidence REAL NOT NULL DEFAULT 0.5,
                imported_utc TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_rumoured_coords ON rumoured_sites(latitude, longitude);
            CREATE INDEX IF NOT EXISTS idx_rumoured_state ON rumoured_sites(state_code);
            """;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpsertAsync(RumouredSite site, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            INSERT INTO rumoured_sites (
                id, external_id, name, state_code, latitude, longitude, minerals_json,
                source_type, source_url, source_label, notes, confidence, imported_utc)
            VALUES ($id,$ext,$name,$state,$lat,$lon,$minerals,$stype,$url,$label,$notes,$conf,$imported)
            ON CONFLICT(id) DO UPDATE SET
                external_id=excluded.external_id, name=excluded.name, state_code=excluded.state_code,
                latitude=excluded.latitude, longitude=excluded.longitude, minerals_json=excluded.minerals_json,
                source_type=excluded.source_type, source_url=excluded.source_url,
                source_label=excluded.source_label, notes=excluded.notes,
                confidence=excluded.confidence, imported_utc=excluded.imported_utc;
            """;
        cmd.Parameters.AddWithValue("$id", site.Id.ToString("N"));
        cmd.Parameters.AddWithValue("$ext", (object?)site.ExternalId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$name", site.Name);
        cmd.Parameters.AddWithValue("$state", site.StateCode);
        cmd.Parameters.AddWithValue("$lat", (object?)site.Coordinates?.LatitudeDegrees ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$lon", (object?)site.Coordinates?.LongitudeDegrees ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$minerals", System.Text.Json.JsonSerializer.Serialize(site.ReportedMinerals));
        cmd.Parameters.AddWithValue("$stype", (int)site.SourceType);
        cmd.Parameters.AddWithValue("$url", (object?)site.SourceUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$label", (object?)site.SourceLabel ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$notes", (object?)site.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$conf", site.Confidence);
        cmd.Parameters.AddWithValue("$imported", (object?)site.ImportedUtc?.ToString("O") ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RumouredSite>> ListWithCoordinatesAsync(
        int limit = 500,
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            SELECT * FROM rumoured_sites
            WHERE latitude IS NOT NULL AND longitude IS NOT NULL
            ORDER BY confidence DESC, name
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 5000));
        return await ReadAllAsync(cmd, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RumouredSite>> FindNearAsync(
        GeoCoordinate center,
        double radiusKm,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        var latDelta = radiusKm / 111.0;
        var lonDelta = radiusKm / (111.0 * Math.Max(0.2, Math.Cos(center.LatitudeRadians)));

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            SELECT * FROM rumoured_sites
            WHERE latitude IS NOT NULL AND longitude IS NOT NULL
              AND latitude BETWEEN $minLat AND $maxLat
              AND longitude BETWEEN $minLon AND $maxLon
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$minLat", center.LatitudeDegrees - latDelta);
        cmd.Parameters.AddWithValue("$maxLat", center.LatitudeDegrees + latDelta);
        cmd.Parameters.AddWithValue("$minLon", center.LongitudeDegrees - lonDelta);
        cmd.Parameters.AddWithValue("$maxLon", center.LongitudeDegrees + lonDelta);
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 2000));

        var boxed = await ReadAllAsync(cmd, cancellationToken).ConfigureAwait(false);
        return boxed
            .Where(s => s.Coordinates is { } c && HaversineKm(center, c) <= radiusKm)
            .OrderBy(s => HaversineKm(center, s.Coordinates!.Value))
            .Take(limit)
            .ToList();
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM rumoured_sites;";
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static double HaversineKm(GeoCoordinate a, GeoCoordinate b)
    {
        const double earthRadiusKm = 6371.0;
        var dLat = b.LatitudeRadians - a.LatitudeRadians;
        var dLon = b.LongitudeRadians - a.LongitudeRadians;
        var sinLat = Math.Sin(dLat / 2);
        var sinLon = Math.Sin(dLon / 2);
        var h = sinLat * sinLat + Math.Cos(a.LatitudeRadians) * Math.Cos(b.LatitudeRadians) * sinLon * sinLon;
        return 2 * earthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    private static async Task<IReadOnlyList<RumouredSite>> ReadAllAsync(
        SqliteCommand cmd,
        CancellationToken cancellationToken)
    {
        var list = new List<RumouredSite>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            list.Add(Map(reader));
        return list;
    }

    private static RumouredSite Map(SqliteDataReader reader)
    {
        GeoCoordinate? coords = null;
        if (!reader.IsDBNull(reader.GetOrdinal("latitude")) && !reader.IsDBNull(reader.GetOrdinal("longitude")))
        {
            coords = new GeoCoordinate(
                reader.GetDouble(reader.GetOrdinal("latitude")),
                reader.GetDouble(reader.GetOrdinal("longitude")));
        }

        var mineralsJson = reader.IsDBNull(reader.GetOrdinal("minerals_json"))
            ? "[]"
            : reader.GetString(reader.GetOrdinal("minerals_json"));
        IReadOnlyList<string> minerals;
        try
        {
            minerals = System.Text.Json.JsonSerializer.Deserialize<List<string>>(mineralsJson) ?? [];
        }
        catch
        {
            minerals = [];
        }

        DateTimeOffset? imported = null;
        var importedOrd = reader.GetOrdinal("imported_utc");
        if (!reader.IsDBNull(importedOrd) &&
            DateTimeOffset.TryParse(reader.GetString(importedOrd), out var parsed))
            imported = parsed;

        return new RumouredSite
        {
            Id = Guid.ParseExact(reader.GetString(reader.GetOrdinal("id")), "N"),
            ExternalId = reader.IsDBNull(reader.GetOrdinal("external_id"))
                ? null
                : reader.GetString(reader.GetOrdinal("external_id")),
            Name = reader.GetString(reader.GetOrdinal("name")),
            StateCode = reader.GetString(reader.GetOrdinal("state_code")),
            Coordinates = coords,
            ReportedMinerals = minerals,
            SourceType = (RumourSourceType)reader.GetInt32(reader.GetOrdinal("source_type")),
            SourceUrl = reader.IsDBNull(reader.GetOrdinal("source_url"))
                ? null
                : reader.GetString(reader.GetOrdinal("source_url")),
            SourceLabel = reader.IsDBNull(reader.GetOrdinal("source_label"))
                ? null
                : reader.GetString(reader.GetOrdinal("source_label")),
            Notes = reader.IsDBNull(reader.GetOrdinal("notes"))
                ? null
                : reader.GetString(reader.GetOrdinal("notes")),
            Confidence = reader.GetDouble(reader.GetOrdinal("confidence")),
            ImportedUtc = imported
        };
    }

    private void EnsureOpen()
    {
        if (_connection is null)
            throw new InvalidOperationException("Call InitializeAsync before using RumouredSiteStore.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }
    }
}
