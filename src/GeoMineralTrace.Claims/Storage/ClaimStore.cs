using System.Globalization;
using GeoMineralTrace.Core.Claims;
using GeoMineralTrace.Core.Geo;
using Microsoft.Data.Sqlite;

namespace GeoMineralTrace.Claims.Storage;

/// <summary>
/// Local-first SQLite store for BLM mining claims. Never initiates network I/O.
/// </summary>
public sealed class ClaimStore : IAsyncDisposable
{
    private readonly string _connectionString;
    private SqliteConnection? _connection;

    public ClaimStore(string databasePath)
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
            CREATE TABLE IF NOT EXISTS mining_claims (
                id TEXT PRIMARY KEY,
                external_id TEXT,
                claim_name TEXT NOT NULL,
                serial_number TEXT NOT NULL,
                legacy_serial TEXT,
                claim_type INTEGER NOT NULL DEFAULT 0,
                status INTEGER NOT NULL DEFAULT 0,
                latitude REAL,
                longitude REAL,
                legal_description TEXT,
                township TEXT,
                range TEXT,
                section TEXT,
                meridian TEXT,
                claimant TEXT,
                location_date TEXT,
                last_fee_paid TEXT,
                last_assessment_year INTEGER,
                state_code TEXT NOT NULL,
                county TEXT,
                field_office TEXT,
                minerals_json TEXT NOT NULL DEFAULT '[]',
                acres REAL,
                source_dataset TEXT,
                source_imported_utc TEXT,
                blm_disposition TEXT,
                maintenance_deadline_approaching INTEGER NOT NULL DEFAULT 0,
                legal_notes TEXT
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ix_claims_external_id ON mining_claims(external_id) WHERE external_id IS NOT NULL;
            CREATE INDEX IF NOT EXISTS ix_claims_state ON mining_claims(state_code);
            CREATE INDEX IF NOT EXISTS ix_claims_status ON mining_claims(status);
            CREATE INDEX IF NOT EXISTS ix_claims_serial ON mining_claims(serial_number);
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
        cmd.CommandText = "SELECT COUNT(*) FROM mining_claims;";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    public async Task UpsertAsync(MiningClaim claim, CancellationToken cancellationToken = default)
    {
        EnsureOpen();

        if (!string.IsNullOrWhiteSpace(claim.ExternalId))
        {
            var existing = await GetByExternalIdAsync(claim.ExternalId, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                claim = CloneWithId(claim, existing.Id);
            }
        }

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            INSERT INTO mining_claims (
                id, external_id, claim_name, serial_number, legacy_serial, claim_type, status,
                latitude, longitude, legal_description, township, range, section, meridian,
                claimant, location_date, last_fee_paid, last_assessment_year,
                state_code, county, field_office, minerals_json, acres,
                source_dataset, source_imported_utc, blm_disposition,
                maintenance_deadline_approaching, legal_notes)
            VALUES (
                $id, $ext, $name, $serial, $legacy, $type, $status,
                $lat, $lon, $legal, $twp, $rng, $sec, $mer,
                $claimant, $located, $fee, $assess,
                $state, $county, $office, $minerals, $acres,
                $source, $imported, $disp, $deadline, $notes)
            ON CONFLICT(id) DO UPDATE SET
                external_id=excluded.external_id,
                claim_name=excluded.claim_name,
                serial_number=excluded.serial_number,
                legacy_serial=excluded.legacy_serial,
                claim_type=excluded.claim_type,
                status=excluded.status,
                latitude=excluded.latitude,
                longitude=excluded.longitude,
                legal_description=excluded.legal_description,
                township=excluded.township,
                range=excluded.range,
                section=excluded.section,
                meridian=excluded.meridian,
                claimant=excluded.claimant,
                location_date=excluded.location_date,
                last_fee_paid=excluded.last_fee_paid,
                last_assessment_year=excluded.last_assessment_year,
                state_code=excluded.state_code,
                county=excluded.county,
                field_office=excluded.field_office,
                minerals_json=excluded.minerals_json,
                acres=excluded.acres,
                source_dataset=excluded.source_dataset,
                source_imported_utc=excluded.source_imported_utc,
                blm_disposition=excluded.blm_disposition,
                maintenance_deadline_approaching=excluded.maintenance_deadline_approaching,
                legal_notes=excluded.legal_notes;
            """;

        AddParams(cmd, claim);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<MiningClaim?> GetByExternalIdAsync(string externalId, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT * FROM mining_claims WHERE external_id = $ext LIMIT 1;";
        cmd.Parameters.AddWithValue("$ext", externalId);
        var rows = await ReadAllAsync(cmd, cancellationToken).ConfigureAwait(false);
        return rows.FirstOrDefault();
    }

    public async Task<MiningClaim?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT * FROM mining_claims WHERE id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", id.ToString("N"));
        var rows = await ReadAllAsync(cmd, cancellationToken).ConfigureAwait(false);
        return rows.FirstOrDefault();
    }

    public async Task<IReadOnlyList<MiningClaim>> QueryAsync(
        string? stateCode = null,
        ClaimStatus? status = null,
        bool? deadlineApproachingOnly = null,
        string? mineral = null,
        int limit = 500,
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        var where = new List<string>();
        if (!string.IsNullOrWhiteSpace(stateCode))
        {
            where.Add("state_code = $state");
            cmd.Parameters.AddWithValue("$state", stateCode.Trim().ToUpperInvariant());
        }

        if (status is { } st)
        {
            where.Add("status = $status");
            cmd.Parameters.AddWithValue("$status", (int)st);
        }

        if (deadlineApproachingOnly == true)
            where.Add("maintenance_deadline_approaching = 1");

        if (!string.IsNullOrWhiteSpace(mineral))
        {
            where.Add("minerals_json LIKE $mineral");
            cmd.Parameters.AddWithValue("$mineral", "%" + mineral.Trim().ToLowerInvariant() + "%");
        }

        var sql = "SELECT * FROM mining_claims";
        if (where.Count > 0)
            sql += " WHERE " + string.Join(" AND ", where);
        sql += " ORDER BY CASE status WHEN 2 THEN 0 WHEN 3 THEN 1 WHEN 1 THEN 2 ELSE 3 END, claim_name LIMIT $limit;";
        cmd.Parameters.AddWithValue("$limit", limit);
        cmd.CommandText = sql;
        return await ReadAllAsync(cmd, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MiningClaim>> FindNearAsync(
        GeoCoordinate center,
        double radiusKm,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        var latDelta = radiusKm / 111.0;
        var lonDelta = radiusKm / (111.0 * Math.Max(0.2, Math.Cos(center.LatitudeRadians)));

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            SELECT * FROM mining_claims
            WHERE latitude IS NOT NULL AND longitude IS NOT NULL
              AND latitude BETWEEN $latMin AND $latMax
              AND longitude BETWEEN $lonMin AND $lonMax
            LIMIT 400;
            """;
        cmd.Parameters.AddWithValue("$latMin", center.LatitudeDegrees - latDelta);
        cmd.Parameters.AddWithValue("$latMax", center.LatitudeDegrees + latDelta);
        cmd.Parameters.AddWithValue("$lonMin", center.LongitudeDegrees - lonDelta);
        cmd.Parameters.AddWithValue("$lonMax", center.LongitudeDegrees + lonDelta);

        var boxed = await ReadAllAsync(cmd, cancellationToken).ConfigureAwait(false);
        return boxed
            .Where(c => c.Coordinates is { } coords && HaversineKm(center, coords) <= radiusKm)
            .OrderBy(c => HaversineKm(center, c.Coordinates!.Value))
            .Take(limit)
            .ToList();
    }

    public async Task<IReadOnlyList<(double Latitude, double Longitude)>> ListActiveCoordinateIndexAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        // Active + ExpiringSoon only — closed claims do not imply current tenure at a mine site.
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            SELECT latitude, longitude FROM mining_claims
            WHERE latitude IS NOT NULL AND longitude IS NOT NULL
              AND status IN (1, 2);
            """;
        var list = new List<(double, double)>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            list.Add((reader.GetDouble(0), reader.GetDouble(1)));
        return list;
    }

    public async Task SeedDemoAsync(CancellationToken cancellationToken = default)
    {
        foreach (var claim in DemoClaims.Create())
            await UpsertAsync(claim, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, int>> CountByStateAsync(CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT state_code, COUNT(*) FROM mining_claims GROUP BY state_code ORDER BY COUNT(*) DESC;";
        var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            dict[reader.GetString(0)] = reader.GetInt32(1);
        return dict;
    }

    public async Task<IReadOnlyDictionary<string, int>> CountByStatusAsync(CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT status, COUNT(*) FROM mining_claims GROUP BY status;";
        var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var status = (ClaimStatus)reader.GetInt32(0);
            dict[ClaimStatusClassifier.StatusBadge(status)] = reader.GetInt32(1);
        }

        return dict;
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
            throw new InvalidOperationException("Call InitializeAsync before using ClaimStore.");
    }

    private async Task ExecuteAsync(string sql, CancellationToken cancellationToken)
    {
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static MiningClaim CloneWithId(MiningClaim claim, Guid id) =>
        new()
        {
            Id = id,
            ExternalId = claim.ExternalId,
            ClaimName = claim.ClaimName,
            SerialNumber = claim.SerialNumber,
            LegacySerialNumber = claim.LegacySerialNumber,
            ClaimType = claim.ClaimType,
            Status = claim.Status,
            Coordinates = claim.Coordinates,
            LegalDescription = claim.LegalDescription,
            Township = claim.Township,
            Range = claim.Range,
            Section = claim.Section,
            Meridian = claim.Meridian,
            ClaimantOfRecord = claim.ClaimantOfRecord,
            LocationDate = claim.LocationDate,
            LastMaintenanceFeePaid = claim.LastMaintenanceFeePaid,
            LastAssessmentYear = claim.LastAssessmentYear,
            StateCode = claim.StateCode,
            County = claim.County,
            FieldOffice = claim.FieldOffice,
            Minerals = claim.Minerals,
            Acres = claim.Acres,
            SourceDataset = claim.SourceDataset,
            SourceImportedUtc = claim.SourceImportedUtc,
            BlmCaseDisposition = claim.BlmCaseDisposition,
            MaintenanceDeadlineApproaching = claim.MaintenanceDeadlineApproaching,
            LegalNotes = claim.LegalNotes
        };

    private static void AddParams(SqliteCommand cmd, MiningClaim claim)
    {
        cmd.Parameters.AddWithValue("$id", claim.Id.ToString("N"));
        cmd.Parameters.AddWithValue("$ext", (object?)claim.ExternalId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$name", claim.ClaimName);
        cmd.Parameters.AddWithValue("$serial", claim.SerialNumber);
        cmd.Parameters.AddWithValue("$legacy", (object?)claim.LegacySerialNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$type", (int)claim.ClaimType);
        cmd.Parameters.AddWithValue("$status", (int)claim.Status);
        cmd.Parameters.AddWithValue("$lat", (object?)claim.Coordinates?.LatitudeDegrees ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$lon", (object?)claim.Coordinates?.LongitudeDegrees ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$legal", (object?)claim.LegalDescription ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$twp", (object?)claim.Township ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$rng", (object?)claim.Range ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sec", (object?)claim.Section ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$mer", (object?)claim.Meridian ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$claimant", (object?)claim.ClaimantOfRecord ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$located", (object?)claim.LocationDate?.ToString("yyyy-MM-dd") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$fee", (object?)claim.LastMaintenanceFeePaid?.ToString("yyyy-MM-dd") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$assess", (object?)claim.LastAssessmentYear ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$state", claim.StateCode);
        cmd.Parameters.AddWithValue("$county", (object?)claim.County ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$office", (object?)claim.FieldOffice ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$minerals", System.Text.Json.JsonSerializer.Serialize(claim.Minerals));
        cmd.Parameters.AddWithValue("$acres", (object?)claim.Acres ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$source", claim.SourceDataset);
        cmd.Parameters.AddWithValue("$imported", (object?)claim.SourceImportedUtc?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$disp", (object?)claim.BlmCaseDisposition ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$deadline", claim.MaintenanceDeadlineApproaching ? 1 : 0);
        cmd.Parameters.AddWithValue("$notes", (object?)claim.LegalNotes ?? DBNull.Value);
    }

    private static async Task<IReadOnlyList<MiningClaim>> ReadAllAsync(SqliteCommand cmd, CancellationToken cancellationToken)
    {
        var list = new List<MiningClaim>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            list.Add(Map(reader));
        return list;
    }

    private static MiningClaim Map(SqliteDataReader reader)
    {
        double? lat = reader.IsDBNull(reader.GetOrdinal("latitude")) ? null : reader.GetDouble(reader.GetOrdinal("latitude"));
        double? lon = reader.IsDBNull(reader.GetOrdinal("longitude")) ? null : reader.GetDouble(reader.GetOrdinal("longitude"));
        GeoCoordinate? coords = lat is { } la && lon is { } lo ? new GeoCoordinate(la, lo) : null;
        var minerals = System.Text.Json.JsonSerializer.Deserialize<List<string>>(
            reader.GetString(reader.GetOrdinal("minerals_json"))) ?? [];

        return new MiningClaim
        {
            Id = Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
            ExternalId = reader.IsDBNull(reader.GetOrdinal("external_id")) ? null : reader.GetString(reader.GetOrdinal("external_id")),
            ClaimName = reader.GetString(reader.GetOrdinal("claim_name")),
            SerialNumber = reader.GetString(reader.GetOrdinal("serial_number")),
            LegacySerialNumber = reader.IsDBNull(reader.GetOrdinal("legacy_serial")) ? null : reader.GetString(reader.GetOrdinal("legacy_serial")),
            ClaimType = (ClaimType)reader.GetInt32(reader.GetOrdinal("claim_type")),
            Status = (ClaimStatus)reader.GetInt32(reader.GetOrdinal("status")),
            Coordinates = coords,
            LegalDescription = reader.IsDBNull(reader.GetOrdinal("legal_description")) ? null : reader.GetString(reader.GetOrdinal("legal_description")),
            Township = reader.IsDBNull(reader.GetOrdinal("township")) ? null : reader.GetString(reader.GetOrdinal("township")),
            Range = reader.IsDBNull(reader.GetOrdinal("range")) ? null : reader.GetString(reader.GetOrdinal("range")),
            Section = reader.IsDBNull(reader.GetOrdinal("section")) ? null : reader.GetString(reader.GetOrdinal("section")),
            Meridian = reader.IsDBNull(reader.GetOrdinal("meridian")) ? null : reader.GetString(reader.GetOrdinal("meridian")),
            ClaimantOfRecord = reader.IsDBNull(reader.GetOrdinal("claimant")) ? null : reader.GetString(reader.GetOrdinal("claimant")),
            LocationDate = ParseDate(reader, "location_date"),
            LastMaintenanceFeePaid = ParseDate(reader, "last_fee_paid"),
            LastAssessmentYear = reader.IsDBNull(reader.GetOrdinal("last_assessment_year")) ? null : reader.GetInt32(reader.GetOrdinal("last_assessment_year")),
            StateCode = reader.GetString(reader.GetOrdinal("state_code")),
            County = reader.IsDBNull(reader.GetOrdinal("county")) ? null : reader.GetString(reader.GetOrdinal("county")),
            FieldOffice = reader.IsDBNull(reader.GetOrdinal("field_office")) ? null : reader.GetString(reader.GetOrdinal("field_office")),
            Minerals = minerals,
            Acres = reader.IsDBNull(reader.GetOrdinal("acres")) ? null : reader.GetDouble(reader.GetOrdinal("acres")),
            SourceDataset = reader.IsDBNull(reader.GetOrdinal("source_dataset")) ? "BLM MLRS" : reader.GetString(reader.GetOrdinal("source_dataset")),
            SourceImportedUtc = reader.IsDBNull(reader.GetOrdinal("source_imported_utc"))
                ? null
                : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("source_imported_utc"))),
            BlmCaseDisposition = reader.IsDBNull(reader.GetOrdinal("blm_disposition")) ? null : reader.GetString(reader.GetOrdinal("blm_disposition")),
            MaintenanceDeadlineApproaching = reader.GetInt32(reader.GetOrdinal("maintenance_deadline_approaching")) == 1,
            LegalNotes = reader.IsDBNull(reader.GetOrdinal("legal_notes")) ? null : reader.GetString(reader.GetOrdinal("legal_notes"))
        };
    }

    private static DateOnly? ParseDate(SqliteDataReader reader, string column)
    {
        if (reader.IsDBNull(reader.GetOrdinal(column)))
            return null;
        return DateOnly.Parse(reader.GetString(reader.GetOrdinal(column)));
    }
}
