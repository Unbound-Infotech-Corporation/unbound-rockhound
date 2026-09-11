namespace GeoMineralTrace.Core.Updates;

/// <summary>Compares dotted version strings for update feeds (3 or 4 segments).</summary>
public static class AppVersionComparer
{
    public static Version Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new Version(0, 0, 0, 0);

        var cleaned = text.Trim().TrimStart('v', 'V');
        // Normalize "0.5.1" → four-part for System.Version when needed
        var parts = cleaned.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 1 && int.TryParse(parts[0], out var majorOnly))
            return new Version(majorOnly, 0, 0, 0);
        if (parts.Length == 2
            && int.TryParse(parts[0], out var maj)
            && int.TryParse(parts[1], out var min))
            return new Version(maj, min, 0, 0);
        if (parts.Length == 3
            && int.TryParse(parts[0], out var a)
            && int.TryParse(parts[1], out var b)
            && int.TryParse(parts[2], out var c))
            return new Version(a, b, c, 0);
        if (Version.TryParse(cleaned, out var full))
            return full;

        return new Version(0, 0, 0, 0);
    }

    /// <summary>True when <paramref name="remote"/> is strictly newer than <paramref name="local"/>.</summary>
    public static bool IsNewer(string? remote, string? local) =>
        Parse(remote).CompareTo(Parse(local)) > 0;

    public static string FormatDisplay(Version? v)
    {
        if (v is null) return "0.0.0";
        return v.Revision > 0
            ? $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}"
            : $"{v.Major}.{v.Minor}.{v.Build}";
    }
}
