using Microsoft.Data.Sqlite;

namespace GeoMineralTrace.Rockhounding.Storage;

/// <summary>
/// Local user ratings and notes that improve the on-device database only.
/// </summary>
public sealed class UserRatingService
{
    private readonly LocalityStore _store;

    public UserRatingService(LocalityStore store) => _store = store;

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        // Re-open via reflection is awkward; expose connection helper on store instead.
        await _store.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS user_ratings (
                id TEXT PRIMARY KEY,
                locality_id TEXT NOT NULL,
                stars REAL NOT NULL,
                notes TEXT,
                created_utc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_user_ratings_locality ON user_ratings(locality_id);
            """, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddRatingAsync(
        Guid localityId,
        double stars,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        stars = Math.Clamp(stars, 0, 5);
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await _store.ExecuteAsync("""
            INSERT INTO user_ratings (id, locality_id, stars, notes, created_utc)
            VALUES ($id, $loc, $stars, $notes, $created);
            """, cancellationToken, cmd =>
        {
            cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            cmd.Parameters.AddWithValue("$loc", localityId.ToString("N"));
            cmd.Parameters.AddWithValue("$stars", stars);
            cmd.Parameters.AddWithValue("$notes", (object?)notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
        }).ConfigureAwait(false);

        await _store.ExecuteAsync("""
            UPDATE localities SET
                user_avg_rating = (SELECT AVG(stars) FROM user_ratings WHERE locality_id = $loc),
                user_rating_count = (SELECT COUNT(*) FROM user_ratings WHERE locality_id = $loc)
            WHERE id = $loc;
            """, cancellationToken, cmd =>
        {
            cmd.Parameters.AddWithValue("$loc", localityId.ToString("N"));
        }).ConfigureAwait(false);
    }
}
