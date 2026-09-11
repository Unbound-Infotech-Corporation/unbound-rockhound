using System.Globalization;
using System.Text;
using GeoMineralTrace.Core.Geo;
using GeoMineralTrace.Core.Rockhounding;
using GeoMineralTrace.Rockhounding.Storage;

namespace GeoMineralTrace.Rockhounding.Import;

/// <summary>
/// Imports attributed rumoured-site CSV (forums, blogs, boards — local-only; no live scraping).
/// Header:
/// name,state_code,latitude,longitude,minerals,source_type,source_url,source_label,notes,confidence,external_id
/// </summary>
public sealed class RumouredSiteCsvImporter
{
    public IReadOnlyList<RumouredSite> Parse(string csvText)
    {
        var lines = csvText.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2)
            return [];

        var header = SplitCsv(lines[0]).Select(h => h.Trim().ToLowerInvariant()).ToArray();
        var list = new List<RumouredSite>();
        var now = DateTimeOffset.UtcNow;

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

            var sourceType = Enum.TryParse<RumourSourceType>(Get("source_type"), true, out var st)
                ? st
                : RumourSourceType.Unknown;

            var confidence = double.TryParse(Get("confidence"), NumberStyles.Float, CultureInfo.InvariantCulture, out var conf)
                ? Math.Clamp(conf, 0, 1)
                : 0.45;

            var externalId = Get("external_id");
            if (string.IsNullOrWhiteSpace(externalId))
            {
                var slug = $"{name}-{state}".ToLowerInvariant();
                foreach (var c in Path.GetInvalidFileNameChars().Concat([' ', ',', '.', '/', '\\', '\'', '"']))
                    slug = slug.Replace(c, '-');
                while (slug.Contains("--", StringComparison.Ordinal))
                    slug = slug.Replace("--", "-", StringComparison.Ordinal);
                externalId = "rumour:" + slug.Trim('-');
            }

            list.Add(new RumouredSite
            {
                Id = Guid.NewGuid(),
                ExternalId = externalId,
                Name = name,
                StateCode = state.ToUpperInvariant(),
                Coordinates = coords,
                ReportedMinerals = minerals,
                SourceType = sourceType,
                SourceUrl = string.IsNullOrWhiteSpace(Get("source_url")) ? null : Get("source_url"),
                SourceLabel = string.IsNullOrWhiteSpace(Get("source_label")) ? null : Get("source_label"),
                Notes = string.IsNullOrWhiteSpace(Get("notes")) ? null : Get("notes"),
                Confidence = confidence,
                ImportedUtc = now
            });
        }

        return list;
    }

    public async Task<int> ImportFileAsync(
        string csvPath,
        RumouredSiteStore store,
        CancellationToken cancellationToken = default)
    {
        var text = await File.ReadAllTextAsync(csvPath, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        var sites = Parse(text);
        var count = 0;
        foreach (var site in sites)
        {
            await store.UpsertAsync(site, cancellationToken).ConfigureAwait(false);
            count++;
        }

        return count;
    }

    private static List<string> SplitCsv(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
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
