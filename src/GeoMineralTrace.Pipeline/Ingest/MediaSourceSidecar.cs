using System.Text.Json;
using GeoMineralTrace.Pipeline.Ner;

namespace GeoMineralTrace.Pipeline.Ingest;

/// <summary>
/// Sidecar written next to downloaded / captured media so the pipeline can NER
/// title, description, and URL even when the container has no EXIF places.
/// </summary>
public sealed class MediaSourceSidecar
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string? SourceUrl { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Platform { get; set; }
    public string? MediaId { get; set; }
    public string? LocationLabel { get; set; }
    public List<string> Tags { get; set; } = [];
    public DateTimeOffset CapturedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public static string PathFor(string mediaPath) =>
        Path.ChangeExtension(mediaPath, ".gmt-source.json");

    public static async Task WriteAsync(string mediaPath, MediaSourceSidecar sidecar, CancellationToken ct = default)
    {
        var path = PathFor(mediaPath);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, sidecar, JsonOptions, ct).ConfigureAwait(false);
    }

    public static async Task<MediaSourceSidecar?> TryReadAsync(string mediaPath, CancellationToken ct = default)
    {
        var path = PathFor(mediaPath);
        if (!File.Exists(path))
            return null;

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<MediaSourceSidecar>(stream, JsonOptions, ct)
                .ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Text blob suitable for place/mineral NER (sanitized — no related-video farms).</summary>
    public string CombinedText() =>
        RemoteDescriptionSanitizer.BuildNerCorpus(Title, Description, Tags, LocationLabel);
}
