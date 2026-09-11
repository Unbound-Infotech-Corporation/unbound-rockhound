namespace GeoMineralTrace.Pipeline.Ingest;

public sealed record SocialMediaCookie(
    string Domain,
    string Name,
    string? Value,
    string Path,
    bool IsSecure,
    long ExpiresUnix);
