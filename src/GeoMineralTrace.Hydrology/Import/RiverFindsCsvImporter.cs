using System.Globalization;
using GeoMineralTrace.Core.Hydrology;
using GeoMineralTrace.Hydrology.Storage;

namespace GeoMineralTrace.Hydrology.Import;

/// <summary>Imports curated river mineral finds from CSV (offline).</summary>
public sealed class RiverFindsCsvImporter
{
    public sealed record ImportResult(int RowsRead, int AssociationsUpserted, int Skipped);

    public async Task<ImportResult> ImportFileAsync(
        string path,
        RiverStore store,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var lines = await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);
        if (lines.Length < 2)
            return new ImportResult(0, 0, 0);

        var header = ParseCsvLine(lines[0]);
        var col = IndexColumns(header);

        var upserted = 0;
        var skipped = 0;

        await store.BeginBulkWriteAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            for (var i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                    continue;

                var fields = ParseCsvLine(line);
                if (!TryParseRow(fields, col, out var externalId, out var mineral, out var citation, out var notes, out var habitat))
                {
                    skipped++;
                    continue;
                }

                var wc = await store.GetByExternalIdAsync(externalId, cancellationToken).ConfigureAwait(false);
                if (wc is null)
                {
                    progress?.Report($"Skip row {i + 1}: unknown river_external_id '{externalId}'");
                    skipped++;
                    continue;
                }

                var fullNotes = habitat is { Length: > 0 }
                    ? string.IsNullOrWhiteSpace(notes) ? habitat : $"{notes} · {habitat}"
                    : notes;

                await store.UpsertMineralAsync(new RiverMineralAssociation
                {
                    WatercourseId = wc.Id,
                    MineralName = mineral,
                    AssociationKind = MineralAssociationKind.Curated,
                    Confidence = AssociationConfidence.High,
                    Citation = citation,
                    Notes = fullNotes
                }, cancellationToken).ConfigureAwait(false);
                upserted++;
            }

            await store.EndBulkWriteAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            try { await store.EndBulkWriteAsync(cancellationToken).ConfigureAwait(false); } catch { /* rollback */ }
            throw;
        }

        progress?.Report($"Curated river finds: {upserted} associations ({skipped} skipped).");
        return new ImportResult(lines.Length - 1, upserted, skipped);
    }

    private static bool TryParseRow(
        IReadOnlyList<string> fields,
        Dictionary<string, int> col,
        out string externalId,
        out string mineral,
        out string? citation,
        out string? notes,
        out string? habitat)
    {
        externalId = "";
        mineral = "";
        citation = null;
        notes = null;
        habitat = null;

        if (!col.TryGetValue("river_external_id", out var extIdx) || extIdx >= fields.Count)
            return false;
        if (!col.TryGetValue("mineral", out var minIdx) || minIdx >= fields.Count)
            return false;

        externalId = fields[extIdx].Trim();
        mineral = fields[minIdx].Trim();
        if (externalId.Length == 0 || mineral.Length == 0)
            return false;

        if (col.TryGetValue("citation", out var citeIdx) && citeIdx < fields.Count)
            citation = NullIfEmpty(fields[citeIdx]);
        if (col.TryGetValue("notes", out var noteIdx) && noteIdx < fields.Count)
            notes = NullIfEmpty(fields[noteIdx]);
        if (col.TryGetValue("typical_habitat", out var habIdx) && habIdx < fields.Count)
            habitat = NullIfEmpty(fields[habIdx]);

        return true;
    }

    private static Dictionary<string, int> IndexColumns(IReadOnlyList<string> header)
    {
        var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Count; i++)
            dict[header[i].Trim().ToLowerInvariant()] = i;
        return dict;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                    inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
                current.Append(c);
        }

        fields.Add(current.ToString());
        return fields;
    }

    private static string? NullIfEmpty(string s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
