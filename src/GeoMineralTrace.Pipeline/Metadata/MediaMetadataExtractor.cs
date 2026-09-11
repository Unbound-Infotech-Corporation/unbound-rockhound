using System.Globalization;
using System.Text.RegularExpressions;
using GeoMineralTrace.Core.Common;
using GeoMineralTrace.Core.Evidence;
using GeoMineralTrace.Core.Geo;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using Directory = MetadataExtractor.Directory;

namespace GeoMineralTrace.Pipeline.Metadata;

/// <summary>
/// Extracts EXIF/GPS/container metadata from images and common video containers.
/// GPS from video is uncommon; when present it is emitted as GpsEmbed evidence.
/// </summary>
public sealed class MediaMetadataExtractor
{
    private static readonly HashSet<string> ImageExt =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".webp", ".heic" };

    private static readonly HashSet<string> VideoExt =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mov", ".m4v", ".avi", ".mkv", ".wmv", ".webm", ".3gp"
        };

    public IReadOnlyList<EvidenceItem> Extract(Guid sessionId, string mediaPath)
    {
        if (!File.Exists(mediaPath))
            throw new FileNotFoundException("Media file not found.", mediaPath);

        var items = new List<EvidenceItem>();
        var ext = Path.GetExtension(mediaPath);
        var fi = new FileInfo(mediaPath);

        items.Add(Make(sessionId, mediaPath, EvidenceType.Metadata,
            $"File: {fi.Name} ({fi.Length:N0} bytes)",
            $"Extension={ext}; Created={fi.CreationTimeUtc:u}; Modified={fi.LastWriteTimeUtc:u}",
            Confidence.VeryHigh,
            "Filesystem metadata — not proof of capture time."));

        try
        {
            IReadOnlyList<Directory> directories = ImageMetadataReader.ReadMetadata(mediaPath);
            foreach (var dir in directories)
            {
                foreach (var tag in dir.Tags)
                {
                    if (string.IsNullOrWhiteSpace(tag.Description))
                        continue;

                    // Keep high-signal tags only to avoid flooding the board
                    if (!IsHighSignal(dir.Name, tag.Name))
                        continue;

                    items.Add(Make(sessionId, mediaPath, EvidenceType.Metadata,
                        $"{dir.Name}: {tag.Name} = {tag.Description}",
                        tag.Description,
                        Confidence.High,
                        $"Source directory: {dir.Name}"));
                }
            }

            var gps = ExtractGps(directories);
            if (gps is { } coord)
            {
                items.Add(new EvidenceItem
                {
                    Id = Guid.NewGuid(),
                    AnalysisSessionId = sessionId,
                    Type = EvidenceType.GpsEmbed,
                    Summary = $"Embedded GPS: {coord}",
                    RawContent = coord.ToString(),
                    SourceMediaPath = mediaPath,
                    Confidence = Confidence.High,
                    Notes = "Embedded GPS may be spoofed, stale, or device-home rather than scene location.",
                    Attributes = new Dictionary<string, string>
                    {
                        ["lat"] = coord.LatitudeDegrees.ToString(CultureInfo.InvariantCulture),
                        ["lon"] = coord.LongitudeDegrees.ToString(CultureInfo.InvariantCulture)
                    }
                });
            }
        }
        catch (ImageProcessingException ex)
        {
            items.Add(Make(sessionId, mediaPath, EvidenceType.Metadata,
                $"Metadata parse limited: {ex.Message}",
                null,
                Confidence.Low,
                VideoExt.Contains(ext)
                    ? "Container may lack EXIF; ffmpeg probe can enrich when available."
                    : "Unrecognized or truncated metadata block."));
        }
        catch (Exception ex)
        {
            items.Add(Make(sessionId, mediaPath, EvidenceType.Metadata,
                $"Metadata extraction error: {ex.GetType().Name}",
                ex.Message,
                Confidence.Low,
                "Non-fatal; pipeline continues."));
        }

        // QuickTime/ISO date hints via filename patterns (weak signal)
        var nameDate = TryParseDateFromFileName(Path.GetFileNameWithoutExtension(mediaPath));
        if (nameDate is { } nd)
        {
            items.Add(Make(sessionId, mediaPath, EvidenceType.Metadata,
                $"Filename date hint: {nd:u}",
                nd.ToString("u"),
                Confidence.Low,
                "Weak signal — filenames are easily renamed."));
        }

        return items;
    }

    private static bool IsHighSignal(string directory, string tagName)
    {
        var d = directory.ToLowerInvariant();
        var t = tagName.ToLowerInvariant();
        if (d.Contains("gps")) return true;
        if (t.Contains("make") || t.Contains("model") || t.Contains("software")) return true;
        if (t.Contains("date") || t.Contains("time") || t.Contains("duration")) return true;
        if (t.Contains("width") || t.Contains("height") || t.Contains("codec") || t.Contains("frame rate")) return true;
        if (t.Contains("latitude") || t.Contains("longitude") || t.Contains("altitude")) return true;
        if (d.Contains("quicktime") && (t.Contains("created") || t.Contains("modified"))) return true;
        return false;
    }

    private static GeoCoordinate? ExtractGps(IReadOnlyList<Directory> directories)
    {
        var gpsDir = directories.OfType<GpsDirectory>().FirstOrDefault();
        if (gpsDir is null)
            return null;

        try
        {
            // Prefer library helper when available; fall back to tag strings.
            dynamic? loc = gpsDir.GetGeoLocation();
            if (loc is not null)
            {
                double lat = (double)loc.Latitude;
                double lon = (double)loc.Longitude;
                if (Math.Abs(lat) > 1e-9 || Math.Abs(lon) > 1e-9)
                {
                    double? alt = null;
                    if (gpsDir.TryGetDouble(GpsDirectory.TagAltitude, out var a))
                        alt = a;
                    return new GeoCoordinate(lat, lon, alt);
                }
            }
        }
        catch
        {
            // Fall through to tag parsing
        }

        return null;
    }

    private static DateTimeOffset? TryParseDateFromFileName(string name)
    {
        // Patterns: 20240621_153045, VID_20240621_153045, 2024-06-21
        var m = Regex.Match(name,
            @"(?<y>20\d{2})[-_]?((?<mo>\d{2})[-_]?(?<d>\d{2}))(?:[-_T]?(?<h>\d{2})[-_]?(?<mi>\d{2})[-_]?(?<s>\d{2}))?");
        if (!m.Success) return null;
        try
        {
            var y = int.Parse(m.Groups["y"].Value);
            var mo = m.Groups["mo"].Success ? int.Parse(m.Groups["mo"].Value) : 1;
            var d = m.Groups["d"].Success ? int.Parse(m.Groups["d"].Value) : 1;
            var h = m.Groups["h"].Success ? int.Parse(m.Groups["h"].Value) : 0;
            var mi = m.Groups["mi"].Success ? int.Parse(m.Groups["mi"].Value) : 0;
            var s = m.Groups["s"].Success ? int.Parse(m.Groups["s"].Value) : 0;
            return new DateTimeOffset(y, mo, d, h, mi, s, TimeSpan.Zero);
        }
        catch
        {
            return null;
        }
    }

    private static EvidenceItem Make(
        Guid sessionId,
        string path,
        EvidenceType type,
        string summary,
        string? raw,
        Confidence confidence,
        string? notes) =>
        new()
        {
            Id = Guid.NewGuid(),
            AnalysisSessionId = sessionId,
            Type = type,
            Summary = summary,
            RawContent = raw,
            SourceMediaPath = path,
            Confidence = confidence,
            Notes = notes
        };
}
