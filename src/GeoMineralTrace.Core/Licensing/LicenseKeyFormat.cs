namespace GeoMineralTrace.Core.Licensing;

public static class LicenseKeyFormat
{
    public static string Normalize(string raw)
    {
        var cleaned = new string(raw.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        if (cleaned.StartsWith("UR", StringComparison.Ordinal))
            cleaned = cleaned[2..];
        if (cleaned.Length != 16)
            throw new InvalidOperationException("License key must look like UR-XXXX-XXXX-XXXX-XXXX.");
        return $"UR-{cleaned[..4]}-{cleaned[4..8]}-{cleaned[8..12]}-{cleaned[12..16]}";
    }
}
