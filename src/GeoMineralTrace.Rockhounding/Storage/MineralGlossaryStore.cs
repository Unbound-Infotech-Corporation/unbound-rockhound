using System.Text.Json;
using GeoMineralTrace.Core.Rockhounding;
using Microsoft.Data.Sqlite;

namespace GeoMineralTrace.Rockhounding.Storage;

/// <summary>
/// Offline SQLite glossary store. Never initiates network I/O.
/// </summary>
public sealed class MineralGlossaryStore : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly string _connectionString;
    private readonly string _imagesRoot;
    private SqliteConnection? _connection;

    public MineralGlossaryStore(string databasePath, string? imagesRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
        _imagesRoot = imagesRoot ?? Path.Combine(
            Path.GetDirectoryName(databasePath) ?? ".",
            "glossary-images");
    }

    public string ImagesRoot => _imagesRoot;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(
            new SqliteConnectionStringBuilder(_connectionString).DataSource)!);
        Directory.CreateDirectory(_imagesRoot);

        _connection = new SqliteConnection(_connectionString);
        await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        const string sql = """
            CREATE TABLE IF NOT EXISTS mineral_species (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                aliases_json TEXT NOT NULL DEFAULT '[]',
                formula TEXT,
                crystal_system TEXT,
                mohs_min REAL,
                mohs_max REAL,
                color_range TEXT,
                luster TEXT,
                ima_status TEXT,
                ima_year INTEGER,
                description TEXT NOT NULL,
                wikidata_id TEXT,
                updated_utc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_mineral_species_name ON mineral_species(name COLLATE NOCASE);
            CREATE INDEX IF NOT EXISTS ix_mineral_species_crystal ON mineral_species(crystal_system COLLATE NOCASE);

            CREATE TABLE IF NOT EXISTS mineral_images (
                id TEXT PRIMARY KEY,
                species_id TEXT NOT NULL REFERENCES mineral_species(id) ON DELETE CASCADE,
                file_path TEXT,
                source_url TEXT NOT NULL,
                license_type TEXT NOT NULL,
                attribution_text TEXT NOT NULL,
                photographer TEXT,
                sort_order INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS ix_mineral_images_species ON mineral_images(species_id);
            """;

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> CountSpeciesAsync(CancellationToken ct = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM mineral_species;";
        var n = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt32(n);
    }

    public async Task<(int WithImages, int TextOnly)> CountImageCoverageAsync(CancellationToken ct = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            SELECT
              SUM(CASE WHEN c > 0 THEN 1 ELSE 0 END),
              SUM(CASE WHEN c = 0 THEN 1 ELSE 0 END)
            FROM (
              SELECT s.id, COUNT(i.id) AS c
              FROM mineral_species s
              LEFT JOIN mineral_images i ON i.species_id = s.id
              GROUP BY s.id
            );
            """;
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false))
            return (0, 0);
        return (r.IsDBNull(0) ? 0 : r.GetInt32(0), r.IsDBNull(1) ? 0 : r.GetInt32(1));
    }

    public async Task UpsertSpeciesAsync(MineralSpecies species, CancellationToken ct = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            INSERT INTO mineral_species (
              id, name, aliases_json, formula, crystal_system, mohs_min, mohs_max,
              color_range, luster, ima_status, ima_year, description, wikidata_id, updated_utc)
            VALUES (
              $id, $name, $aliases, $formula, $crystal, $mohsMin, $mohsMax,
              $color, $luster, $imaStatus, $imaYear, $description, $wikidata, $updated)
            ON CONFLICT(id) DO UPDATE SET
              name=excluded.name,
              aliases_json=excluded.aliases_json,
              formula=excluded.formula,
              crystal_system=excluded.crystal_system,
              mohs_min=excluded.mohs_min,
              mohs_max=excluded.mohs_max,
              color_range=excluded.color_range,
              luster=excluded.luster,
              ima_status=excluded.ima_status,
              ima_year=excluded.ima_year,
              description=excluded.description,
              wikidata_id=excluded.wikidata_id,
              updated_utc=excluded.updated_utc;
            """;
        cmd.Parameters.AddWithValue("$id", species.Id);
        cmd.Parameters.AddWithValue("$name", species.Name);
        cmd.Parameters.AddWithValue("$aliases", JsonSerializer.Serialize(species.Aliases));
        cmd.Parameters.AddWithValue("$formula", (object?)species.Formula ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$crystal", (object?)species.CrystalSystem ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$mohsMin", (object?)species.MohsMin ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$mohsMax", (object?)species.MohsMax ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$color", (object?)species.ColorRange ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$luster", (object?)species.Luster ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$imaStatus", (object?)species.ImaStatus ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$imaYear", (object?)species.ImaYear ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$description", species.Description);
        cmd.Parameters.AddWithValue("$wikidata", (object?)species.WikidataId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$updated", species.UpdatedUtc.UtcDateTime.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task ReplaceImagesAsync(string speciesId, IReadOnlyList<MineralImage> images, CancellationToken ct = default)
    {
        EnsureOpen();
        await using (var del = _connection!.CreateCommand())
        {
            del.CommandText = "DELETE FROM mineral_images WHERE species_id = $id;";
            del.Parameters.AddWithValue("$id", speciesId);
            await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var img in images)
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO mineral_images (
                  id, species_id, file_path, source_url, license_type, attribution_text, photographer, sort_order)
                VALUES ($id, $species, $path, $url, $license, $attr, $photo, $ord);
                """;
            cmd.Parameters.AddWithValue("$id", img.Id);
            cmd.Parameters.AddWithValue("$species", speciesId);
            cmd.Parameters.AddWithValue("$path", (object?)img.FilePath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$url", img.SourceUrl);
            cmd.Parameters.AddWithValue("$license", img.LicenseType);
            cmd.Parameters.AddWithValue("$attr", img.AttributionText);
            cmd.Parameters.AddWithValue("$photo", (object?)img.Photographer ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$ord", img.SortOrder);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<MineralSpecies>> SearchAsync(
        MineralSpeciesFilter filter,
        Func<string, Task<IReadOnlyList<string>>>? mineralNamesForState = null,
        CancellationToken ct = default)
    {
        EnsureOpen();
        HashSet<string>? stateAllowed = null;
        if (!string.IsNullOrWhiteSpace(filter.StateCode) && mineralNamesForState is not null)
        {
            var names = await mineralNamesForState(filter.StateCode.Trim()).ConfigureAwait(false);
            stateAllowed = new HashSet<string>(names.Select(NormalizeName), StringComparer.OrdinalIgnoreCase);
        }

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            SELECT s.*,
              (SELECT COUNT(*) FROM mineral_images i WHERE i.species_id = s.id) AS image_count
            FROM mineral_species s
            ORDER BY s.name COLLATE NOCASE;
            """;
        var list = new List<MineralSpecies>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            var species = ReadSpecies(r);
            if (!MatchesFilter(species, filter, stateAllowed))
                continue;
            list.Add(species);
        }

        return list;
    }

    public async Task<MineralSpecies?> GetByIdOrNameAsync(string idOrName, CancellationToken ct = default)
    {
        EnsureOpen();
        var key = idOrName.Trim();
        if (string.IsNullOrEmpty(key))
            return null;

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            SELECT s.*,
              (SELECT COUNT(*) FROM mineral_images i WHERE i.species_id = s.id) AS image_count
            FROM mineral_species s
            WHERE s.id = $q OR s.name = $q COLLATE NOCASE
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$q", key);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (await r.ReadAsync(ct).ConfigureAwait(false))
            return ReadSpecies(r);

        // Alias lookup
        var all = await SearchAsync(new MineralSpeciesFilter(), ct: ct).ConfigureAwait(false);
        var norm = NormalizeName(key);
        return all.FirstOrDefault(s =>
            string.Equals(NormalizeName(s.Name), norm, StringComparison.OrdinalIgnoreCase)
            || s.Aliases.Any(a => string.Equals(NormalizeName(a), norm, StringComparison.OrdinalIgnoreCase)));
    }

    public async Task<IReadOnlyList<MineralImage>> ListImagesAsync(string speciesId, CancellationToken ct = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            SELECT id, species_id, file_path, source_url, license_type, attribution_text, photographer, sort_order
            FROM mineral_images
            WHERE species_id = $id
            ORDER BY sort_order, id;
            """;
        cmd.Parameters.AddWithValue("$id", speciesId);
        var list = new List<MineralImage>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            var rel = r.IsDBNull(2) ? null : r.GetString(2);
            string? abs = null;
            if (!string.IsNullOrWhiteSpace(rel))
            {
                abs = Path.IsPathRooted(rel)
                    ? rel
                    : Path.Combine(_imagesRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(abs))
                    abs = null;
            }

            list.Add(new MineralImage
            {
                Id = r.GetString(0),
                SpeciesId = r.GetString(1),
                FilePath = abs,
                SourceUrl = r.GetString(3),
                LicenseType = r.GetString(4),
                AttributionText = r.GetString(5),
                Photographer = r.IsDBNull(6) ? null : r.GetString(6),
                SortOrder = r.GetInt32(7)
            });
        }

        return list;
    }

    public async Task<IReadOnlyList<MineralImage>> ListAllAttributionsAsync(CancellationToken ct = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            SELECT id, species_id, file_path, source_url, license_type, attribution_text, photographer, sort_order
            FROM mineral_images
            ORDER BY species_id, sort_order, id;
            """;
        var list = new List<MineralImage>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new MineralImage
            {
                Id = r.GetString(0),
                SpeciesId = r.GetString(1),
                FilePath = r.IsDBNull(2) ? null : r.GetString(2),
                SourceUrl = r.GetString(3),
                LicenseType = r.GetString(4),
                AttributionText = r.GetString(5),
                Photographer = r.IsDBNull(6) ? null : r.GetString(6),
                SortOrder = r.GetInt32(7)
            });
        }

        return list;
    }

    public async Task<IReadOnlyList<string>> ListCrystalSystemsAsync(CancellationToken ct = default)
    {
        EnsureOpen();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            SELECT DISTINCT crystal_system FROM mineral_species
            WHERE crystal_system IS NOT NULL AND trim(crystal_system) != ''
            ORDER BY crystal_system COLLATE NOCASE;
            """;
        var list = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            list.Add(r.GetString(0));
        return list;
    }

    public static string Slugify(string name)
    {
        var chars = name.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        var slug = new string(chars);
        while (slug.Contains("--", StringComparison.Ordinal))
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        return slug.Trim('-');
    }

    public static string NormalizeName(string name) =>
        new string(name.Trim().ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-').ToArray())
            .Replace("  ", " ", StringComparison.Ordinal);

    private static bool MatchesFilter(MineralSpecies s, MineralSpeciesFilter f, HashSet<string>? stateAllowed)
    {
        if (stateAllowed is not null)
        {
            var ok = stateAllowed.Contains(NormalizeName(s.Name))
                     || s.Aliases.Any(a => stateAllowed.Contains(NormalizeName(a)));
            if (!ok) return false;
        }

        if (!string.IsNullOrWhiteSpace(f.NameQuery))
        {
            var q = f.NameQuery.Trim();
            if (s.Name.Contains(q, StringComparison.OrdinalIgnoreCase) is false
                && s.Aliases.All(a => a.Contains(q, StringComparison.OrdinalIgnoreCase) is false)
                && (s.Formula?.Contains(q, StringComparison.OrdinalIgnoreCase) is not true))
                return false;
        }

        if (!string.IsNullOrWhiteSpace(f.CrystalSystem)
            && !string.Equals(s.CrystalSystem, f.CrystalSystem, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(f.ColorContains)
            && (s.ColorRange?.Contains(f.ColorContains, StringComparison.OrdinalIgnoreCase) is not true))
            return false;

        if (f.MohsMin is not null && (s.MohsMax is null || s.MohsMax < f.MohsMin))
            return false;
        if (f.MohsMax is not null && (s.MohsMin is null || s.MohsMin > f.MohsMax))
            return false;

        return true;
    }

    private static MineralSpecies ReadSpecies(SqliteDataReader r)
    {
        var aliasesJson = r.GetString(r.GetOrdinal("aliases_json"));
        var aliases = JsonSerializer.Deserialize<List<string>>(aliasesJson, JsonOpts) ?? [];
        return new MineralSpecies
        {
            Id = r.GetString(r.GetOrdinal("id")),
            Name = r.GetString(r.GetOrdinal("name")),
            Aliases = aliases,
            Formula = r.IsDBNull(r.GetOrdinal("formula")) ? null : r.GetString(r.GetOrdinal("formula")),
            CrystalSystem = r.IsDBNull(r.GetOrdinal("crystal_system")) ? null : r.GetString(r.GetOrdinal("crystal_system")),
            MohsMin = r.IsDBNull(r.GetOrdinal("mohs_min")) ? null : r.GetDouble(r.GetOrdinal("mohs_min")),
            MohsMax = r.IsDBNull(r.GetOrdinal("mohs_max")) ? null : r.GetDouble(r.GetOrdinal("mohs_max")),
            ColorRange = r.IsDBNull(r.GetOrdinal("color_range")) ? null : r.GetString(r.GetOrdinal("color_range")),
            Luster = r.IsDBNull(r.GetOrdinal("luster")) ? null : r.GetString(r.GetOrdinal("luster")),
            ImaStatus = r.IsDBNull(r.GetOrdinal("ima_status")) ? null : r.GetString(r.GetOrdinal("ima_status")),
            ImaYear = r.IsDBNull(r.GetOrdinal("ima_year")) ? null : r.GetInt32(r.GetOrdinal("ima_year")),
            Description = r.GetString(r.GetOrdinal("description")),
            WikidataId = r.IsDBNull(r.GetOrdinal("wikidata_id")) ? null : r.GetString(r.GetOrdinal("wikidata_id")),
            UpdatedUtc = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("updated_utc"))),
            ImageCount = r.GetInt32(r.GetOrdinal("image_count"))
        };
    }

    private void EnsureOpen()
    {
        if (_connection is null)
            throw new InvalidOperationException("Call InitializeAsync first.");
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
