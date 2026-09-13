namespace GeoMineralTrace.Core.Licensing;

public static class LicenseKeyFormat
{
    public const string Example = "UR-XXXX-XXXX-XXXX-XXXX";

    public static bool TryNormalize(string? raw, out string normalized, out string? error)
    {
        normalized = "";
        error = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            error = $"Paste a license key shaped like {Example}.";
            return false;
        }

        var cleaned = new string(raw.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        if (cleaned.StartsWith("UR", StringComparison.Ordinal))
            cleaned = cleaned[2..];
        if (cleaned.Length != 16)
        {
            error = $"License key must look like {Example} (16 characters after UR).";
            return false;
        }

        normalized = $"UR-{cleaned[..4]}-{cleaned[4..8]}-{cleaned[8..12]}-{cleaned[12..16]}";
        return true;
    }

    public static string Normalize(string raw)
    {
        if (!TryNormalize(raw, out var normalized, out var error))
            throw new InvalidOperationException(error);
        return normalized;
    }

    public static bool LooksComplete(string? raw) =>
        TryNormalize(raw, out _, out _);
}
