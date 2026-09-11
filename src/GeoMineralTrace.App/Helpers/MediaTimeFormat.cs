using System.Globalization;
using System.Text.RegularExpressions;

namespace GeoMineralTrace_App.Helpers;

public static partial class MediaTimeFormat
{
    [GeneratedRegex(@"^(?:(\d+):)?(\d{1,2}):(\d{2})(?:\.(\d{1,3}))?$", RegexOptions.Compiled)]
    private static partial Regex HmsPattern();

    [GeneratedRegex(@"^(\d{1,2}):(\d{2})(?:\.(\d{1,3}))?$", RegexOptions.Compiled)]
    private static partial Regex MsPattern();

    public static string Format(double totalSeconds)
    {
        if (double.IsNaN(totalSeconds) || totalSeconds < 0) totalSeconds = 0;
        var ts = TimeSpan.FromSeconds(totalSeconds);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
    }

    public static bool TryParse(string? text, out double seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        text = text.Trim();
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds)
            && seconds >= 0)
            return true;

        var hms = HmsPattern().Match(text);
        if (hms.Success)
        {
            var hours = hms.Groups[1].Success ? int.Parse(hms.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
            var minutes = int.Parse(hms.Groups[2].Value, CultureInfo.InvariantCulture);
            var secs = int.Parse(hms.Groups[3].Value, CultureInfo.InvariantCulture);
            var ms = hms.Groups[4].Success
                ? int.Parse(hms.Groups[4].Value.PadRight(3, '0')[..3], CultureInfo.InvariantCulture)
                : 0;
            seconds = hours * 3600 + minutes * 60 + secs + ms / 1000.0;
            return true;
        }

        var msOnly = MsPattern().Match(text);
        if (msOnly.Success)
        {
            var minutes = int.Parse(msOnly.Groups[1].Value, CultureInfo.InvariantCulture);
            var secs = int.Parse(msOnly.Groups[2].Value, CultureInfo.InvariantCulture);
            var ms = msOnly.Groups[3].Success
                ? int.Parse(msOnly.Groups[3].Value.PadRight(3, '0')[..3], CultureInfo.InvariantCulture)
                : 0;
            seconds = minutes * 60 + secs + ms / 1000.0;
            return true;
        }

        return false;
    }
}
