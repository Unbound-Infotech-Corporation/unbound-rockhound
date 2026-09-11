using GeoMineralTrace.Core.Finds;
using GeoMineralTrace.Core.Geo;
using Microsoft.Data.Sqlite;

namespace GeoMineralTrace.Rockhounding.Storage;

/// <summary>Local SQLite store for personal finds. Never initiates network I/O.</summary>
public sealed class PersonalFindStore : IAsyncDisposable
{
    private readonly string _connectionString;
    private SqliteConnection? _connection;

    public PersonalFindStore(string databasePath)
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
            CREATE TABLE IF NOT EXISTS personal_finds (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                latitude REAL,
                longitude REAL,
                state_code TEXT,
                minerals_json TEXT NOT NULL DEFAULT '[]',
                notes TEXT,
                found_on TEXT,
                created_utc TEXT,
                linked_locality TEXT
            );
            """;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpsertAsync(PersonalFind find, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            INSERT INTO personal_finds (id,name,latitude,longitude,state_code,minerals_json,notes,found_on,created_utc,linked_locality)
            VALUES ($id,$name,$lat,$lon,$state,$minerals,$notes,$found,$created,$linked)
            ON CONFLICT(id) DO UPDATE SET
                name=excluded.name, latitude=excluded.latitude, longitude=excluded.longitude,
                state_code=excluded.state_code, minerals_json=excluded.minerals_json, notes=excluded.notes,
                found_on=excluded.found_on, created_utc=excluded.created_utc, linked_locality=excluded.linked_locality;
            """;
        cmd.Parameters.AddWithValue("$id", find.Id.ToString("N"));
        cmd.Parameters.AddWithValue("$name", find.Name);
        cmd.Parameters.AddWithValue("$lat", (object?)find.Coordinates?.LatitudeDegrees ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$lon", (object?)find.Coordinates?.LongitudeDegrees ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$state", (object?)find.StateCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$minerals", System.Text.Json.JsonSerializer.Serialize(find.Minerals));
        cmd.Parameters.AddWithValue("$notes", (object?)find.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$found", (object?)find.FoundOn?.ToString("yyyy-MM-dd") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created", (object?)find.CreatedUtc?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$linked", (object?)find.LinkedLocalityName ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PersonalFind>> ListAsync(CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT * FROM personal_finds ORDER BY created_utc DESC, name;";
        var list = new List<PersonalFind>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            list.Add(Map(reader));
        return list;
    }

    public async Task<PersonalFind?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT * FROM personal_finds WHERE id=$id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", id.ToString("N"));
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Map(reader) : null;
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
            throw new InvalidOperationException("Call InitializeAsync before using PersonalFindStore.");
    }

    private static PersonalFind Map(SqliteDataReader reader)
    {
        double? lat = reader.IsDBNull(reader.GetOrdinal("latitude")) ? null : reader.GetDouble(reader.GetOrdinal("latitude"));
        double? lon = reader.IsDBNull(reader.GetOrdinal("longitude")) ? null : reader.GetDouble(reader.GetOrdinal("longitude"));
        var minerals = System.Text.Json.JsonSerializer.Deserialize<List<string>>(
            reader.GetString(reader.GetOrdinal("minerals_json"))) ?? [];
        return new PersonalFind
        {
            Id = Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
            Name = reader.GetString(reader.GetOrdinal("name")),
            Coordinates = lat is { } la && lon is { } lo ? new GeoCoordinate(la, lo) : null,
            StateCode = reader.IsDBNull(reader.GetOrdinal("state_code")) ? null : reader.GetString(reader.GetOrdinal("state_code")),
            Minerals = minerals,
            Notes = reader.IsDBNull(reader.GetOrdinal("notes")) ? null : reader.GetString(reader.GetOrdinal("notes")),
            FoundOn = reader.IsDBNull(reader.GetOrdinal("found_on"))
                ? null
                : DateOnly.Parse(reader.GetString(reader.GetOrdinal("found_on"))),
            CreatedUtc = reader.IsDBNull(reader.GetOrdinal("created_utc"))
                ? null
                : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("created_utc"))),
            LinkedLocalityName = reader.IsDBNull(reader.GetOrdinal("linked_locality"))
                ? null
                : reader.GetString(reader.GetOrdinal("linked_locality"))
        };
    }
}
