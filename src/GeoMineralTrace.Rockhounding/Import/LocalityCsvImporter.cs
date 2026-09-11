using System.Globalization;
using System.Text;
using GeoMineralTrace.Core.Geo;
using GeoMineralTrace.Core.Rockhounding;
using GeoMineralTrace.Rockhounding.Scoring;

namespace GeoMineralTrace.Rockhounding.Import;

/// <summary>
/// Imports attributed locality seed CSV files (local-only; no network).
/// Expected header:
/// name,state_code,county,latitude,longitude,minerals,land_type,access_status,difficulty,access_notes,sources
/// </summary>
public sealed class LocalityCsvImporter
{
    public IReadOnlyList<Locality> Parse(string csvText)
    {
        var lines = csvText.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2)
            return [];

        var header = SplitCsv(lines[0]).Select(h => h.Trim().ToLowerInvariant()).ToArray();
        var list = new List<Locality>();

        for (var i = 1; i < lines.Length; i++)
        {
            var cols = SplitCsv(lines[i]);
            string Get(string name)
            {
                var idx = Array.IndexOf(header, name);
                return idx >= 0 && idx < cols.Count ? cols[idx].Trim() : "";
            }

            var name = Get("name");
            var state = Get("state_code");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(state))
                continue;

            double? lat = double.TryParse(Get("latitude"), NumberStyles.Float, CultureInfo.InvariantCulture, out var la) ? la : null;
            double? lon = double.TryParse(Get("longitude"), NumberStyles.Float, CultureInfo.InvariantCulture, out var lo) ? lo : null;
            GeoCoordinate? coords = lat is { } a && lon is { } b ? new GeoCoordinate(a, b) : null;

            var minerals = Get("minerals")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var access = Enum.TryParse<AccessStatus>(Get("access_status"), true, out var ac) ? ac : AccessStatus.Unknown;
            var land = Enum.TryParse<LandType>(Get("land_type"), true, out var lt) ? lt : LandType.Unknown;
            var diff = Enum.TryParse<DifficultyLevel>(Get("difficulty"), true, out var df) ? df : DifficultyLevel.Unknown;
            var sources = Get("sources")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var rating = LocalityRatingCalculator.Create(
                accessibility: 6,
                productivity: 6,
                legalClarity: LocalityRatingCalculator.LegalClarityFromAccess(access),
                recency: 6,
                beginnerFriendliness: LocalityRatingCalculator.BeginnerScore(diff, access),
                variety: Math.Min(10, minerals.Length * 2),
                safety: access == AccessStatus.Closed ? 2 : 6);

            var externalId = Get("external_id");
            if (string.IsNullOrWhiteSpace(externalId))
            {
                var slug = $"{name}-{state}".ToLowerInvariant();
                foreach (var c in Path.GetInvalidFileNameChars().Concat([' ', ',', '.', '/', '\\', '\'', '"']))
                    slug = slug.Replace(c, '-');
                while (slug.Contains("--", StringComparison.Ordinal))
                    slug = slug.Replace("--", "-", StringComparison.Ordinal);
                externalId = "curated:" + slug.Trim('-');
            }

            list.Add(new Locality
            {
                Id = Guid.NewGuid(),
                ExternalId = externalId,
                Name = name,
                StateCode = state.ToUpperInvariant(),
                County = string.IsNullOrWhiteSpace(Get("county")) ? null : Get("county"),
                Coordinates = coords,
                ReportedMinerals = minerals,
                LandType = land,
                AccessStatus = access,
                Difficulty = diff,
                AccessNotes = string.IsNullOrWhiteSpace(Get("access_notes")) ? null : Get("access_notes"),
                Sources = sources,
                SystemRating = rating,
                CollectingLimits = "Verify land status and collecting limits before visiting.",
                LastVerifiedUtc = DateTimeOffset.UtcNow,
                SourceDataset = "Curated public/private mines",
                SourceVintage = DateTime.UtcNow.Year.ToString(CultureInfo.InvariantCulture),
                HumanActivityDataMayBeOutdated = false
            });
        }

        return list;
    }

    public async Task<int> ImportFileAsync(
        string csvPath,
        Storage.LocalityStore store,
        CancellationToken cancellationToken = default)
    {
        var text = await File.ReadAllTextAsync(csvPath, cancellationToken).ConfigureAwait(false);
        var localities = Parse(text);
        foreach (var loc in localities)
            await store.UpsertAsync(loc, cancellationToken).ConfigureAwait(false);
        return localities.Count;
    }

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
