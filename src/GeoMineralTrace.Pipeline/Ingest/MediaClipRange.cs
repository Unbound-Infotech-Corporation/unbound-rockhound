namespace GeoMineralTrace.Pipeline.Ingest;

/// <summary>Time-bounded segment of remote media for yt-dlp section download.</summary>
public sealed record MediaClipRange(double StartSeconds, double EndSeconds)
{
    /// <summary>Minimum length for shadow/keyframe analysis (≈ a few frames at 24fps).</summary>
    public const double MinimumDurationSeconds = 2.0;

    public double DurationSeconds => EndSeconds - StartSeconds;

    public MediaClipRange Normalized() =>
        StartSeconds <= EndSeconds
            ? this
            : new MediaClipRange(EndSeconds, StartSeconds);

    public void Validate(double? videoDurationSeconds = null)
    {
        var clip = Normalized();
        if (clip.StartSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(StartSeconds), "In point cannot be negative.");

        if (videoDurationSeconds is > 0 && clip.EndSeconds > videoDurationSeconds.Value + 0.05)
            throw new ArgumentOutOfRangeException(nameof(EndSeconds), "Out point exceeds video duration.");

        if (clip.DurationSeconds < MinimumDurationSeconds)
        {
            throw new ArgumentException(
                $"Selected range is too short ({clip.DurationSeconds:F1}s). Choose at least {MinimumDurationSeconds:F0}s for reliable shadow detection.",
                nameof(EndSeconds));
        }
    }

    /// <summary>yt-dlp --download-sections value, e.g. <c>*0:15-0:45</c>.</summary>
    public string ToDownloadSectionSpec() =>
        $"*{FormatYtDlpTimestamp(Normalized().StartSeconds)}-{FormatYtDlpTimestamp(Normalized().EndSeconds)}";

    public static string FormatYtDlpTimestamp(double totalSeconds)
    {
        if (totalSeconds < 0) totalSeconds = 0;
        var ts = TimeSpan.FromSeconds(totalSeconds);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}"
            : $"{ts.Minutes}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
    }
}
