using GeoMineralTrace.Core.App;

namespace GeoMineralTrace.Pipeline.Ingest;

/// <summary>Persists in-browser screen captures (MediaRecorder blobs) for pipeline ingest.</summary>
public static class CaptureRecordingStore
{
    public static string CaptureDirectory => AppDataPaths.Sub("capture-cache");

    public static string ExtensionForMime(string? mimeType)
    {
        if (string.IsNullOrWhiteSpace(mimeType))
            return ".webm";
        if (mimeType.Contains("mp4", StringComparison.OrdinalIgnoreCase))
            return ".mp4";
        return ".webm";
    }

    public static async Task<string> SaveRecordingAsync(
        byte[] data,
        string? mimeType,
        string? namePrefix = null,
        CancellationToken cancellationToken = default)
    {
        if (data.Length == 0)
            throw new ArgumentException("Recording is empty.", nameof(data));

        Directory.CreateDirectory(CaptureDirectory);
        var ext = ExtensionForMime(mimeType);
        var prefix = string.IsNullOrWhiteSpace(namePrefix) ? "capture" : namePrefix;
        var fileName = $"{prefix}_{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}{ext}";
        var path = Path.Combine(CaptureDirectory, fileName);
        await File.WriteAllBytesAsync(path, data, cancellationToken).ConfigureAwait(false);
        return path;
    }
}
