using System.Globalization;
using GeoMineralTrace.Core.Common;
using GeoMineralTrace.Core.Evidence;
using GeoMineralTrace.Core.Solar;

namespace GeoMineralTrace.Solar;

/// <summary>
/// Rehydrates <see cref="ShadowMeasurement"/> rows saved on the Evidence Board
/// (Solar page or prior pipeline passes).
/// </summary>
public static class ShadowMeasurementParser
{
    public static IReadOnlyList<ShadowMeasurement> ParseFromEvidence(
        Guid sessionId,
        IReadOnlyList<EvidenceItem> evidence,
        DateTimeOffset? defaultObservationUtc = null)
    {
        var list = new List<ShadowMeasurement>();
        foreach (var item in evidence.Where(e => e.Type == EvidenceType.ShadowMeasurement && !e.IsRejected))
        {
            var m = TryParseItem(sessionId, item, defaultObservationUtc);
            if (m is not null)
                list.Add(m);
        }

        return list;
    }

    private static ShadowMeasurement? TryParseItem(
        Guid sessionId,
        EvidenceItem item,
        DateTimeOffset? defaultObservationUtc)
    {
        if (!TryReadDouble(item, "objectHeight", out var objectHeight) &&
            !TryParseRawPair(item.RawContent, out objectHeight, out _))
            return null;

        if (!TryReadDouble(item, "shadowLength", out var shadowLength) &&
            !TryParseRawPair(item.RawContent, out _, out shadowLength))
            return null;

        if (objectHeight <= 0 || shadowLength <= 0)
            return null;

        double? azimuth = null;
        if (item.Attributes?.TryGetValue("azimuthDeg", out var azRaw) == true
            && !string.IsNullOrWhiteSpace(azRaw)
            && double.TryParse(azRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var az))
            azimuth = az;

        var observationUtc = defaultObservationUtc ?? DateTimeOffset.UtcNow;
        if (item.MediaTimestamp is { } ts && defaultObservationUtc is { } baseUtc)
        {
            var dayStart = baseUtc.ToUniversalTime()
                .Subtract(TimeSpan.FromTicks(baseUtc.TimeOfDay.Ticks));
            observationUtc = dayStart.Add(ts);
        }

        return new ShadowMeasurement
        {
            Id = item.Id,
            AnalysisSessionId = item.AnalysisSessionId == Guid.Empty ? sessionId : item.AnalysisSessionId,
            EvidenceId = item.Id,
            SourceMediaPath = item.SourceMediaPath ?? item.PreviewAssetPath ?? "(evidence)",
            MediaTimestamp = item.MediaTimestamp,
            FrameIndex = item.FrameIndex,
            ObservationUtc = observationUtc,
            ObjectHeight = objectHeight,
            ShadowLength = shadowLength,
            ShadowAzimuthDegrees = azimuth,
            MeasurementConfidence = item.Confidence,
            Notes = item.Notes
        };
    }

    private static bool TryReadDouble(EvidenceItem item, string key, out double value)
    {
        value = 0;
        if (item.Attributes is null || !item.Attributes.TryGetValue(key, out var raw))
            return false;
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseRawPair(string? raw, out double height, out double length)
    {
        height = length = 0;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var parts = raw.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            return false;

        return double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out height)
               && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out length);
    }
}
