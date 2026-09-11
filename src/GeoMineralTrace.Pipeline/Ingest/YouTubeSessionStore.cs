using System.Globalization;
using System.Text;
using GeoMineralTrace.Core.App;

namespace GeoMineralTrace.Pipeline.Ingest;

/// <summary>
/// Local Netscape cookie jar used by yt-dlp for authenticated YouTube sessions
/// (age-gated / Premium-account streams the signed-in user can already watch).
/// </summary>
public static class YouTubeSessionStore
{
    public static string DataDirectory => AppDataPaths.LocalRoot;

    public static string CookieFilePath => Path.Combine(DataDirectory, "youtube-cookies.txt");

    public static bool HasSavedSession =>
        File.Exists(CookieFilePath) && new FileInfo(CookieFilePath).Length > 64;

    public static DateTimeOffset? LastSavedUtc
    {
        get
        {
            if (!File.Exists(CookieFilePath)) return null;
            return new FileInfo(CookieFilePath).LastWriteTimeUtc;
        }
    }

    public static void ClearSession()
    {
        try
        {
            if (File.Exists(CookieFilePath))
                File.Delete(CookieFilePath);
        }
        catch
        {
            // ignore
        }
    }

    public static void WriteNetscapeCookies(IEnumerable<SocialMediaCookie> cookies)
    {
        Directory.CreateDirectory(DataDirectory);
        var sb = new StringBuilder();
        sb.AppendLine("# Netscape HTTP Cookie File");
        sb.AppendLine("# GeoMineral Trace — YouTube session (local only). Sign out to delete.");
        sb.AppendLine();

        var written = 0;
        foreach (var c in cookies)
        {
            if (string.IsNullOrWhiteSpace(c.Domain) || string.IsNullOrWhiteSpace(c.Name))
                continue;

            var domain = c.Domain.StartsWith('.') ? c.Domain : "." + c.Domain.TrimStart('.');
            // Keep Google/YouTube auth domains only.
            if (!IsYouTubeRelatedDomain(domain))
                continue;

            var includeSubdomains = domain.StartsWith('.') ? "TRUE" : "FALSE";
            var path = string.IsNullOrWhiteSpace(c.Path) ? "/" : c.Path;
            var secure = c.IsSecure ? "TRUE" : "FALSE";
            var expires = c.ExpiresUnix <= 0
                ? "0"
                : c.ExpiresUnix.ToString(CultureInfo.InvariantCulture);

            sb.Append(domain).Append('\t')
                .Append(includeSubdomains).Append('\t')
                .Append(path).Append('\t')
                .Append(secure).Append('\t')
                .Append(expires).Append('\t')
                .Append(c.Name).Append('\t')
                .Append(c.Value ?? "").AppendLine();
            written++;
        }

        if (written == 0)
            throw new InvalidOperationException("No YouTube/Google cookies were captured. Sign in fully, then click Save session.");

        File.WriteAllText(CookieFilePath, sb.ToString(), Encoding.UTF8);
    }

    public static bool LooksSignedIn(IEnumerable<SocialMediaCookie> cookies)
    {
        var names = cookies
            .Where(c => IsYouTubeRelatedDomain(c.Domain))
            .Select(c => c.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return names.Contains("LOGIN_INFO")
               || names.Contains("SAPISID")
               || names.Contains("SID")
               || names.Contains("__Secure-1PSID")
               || names.Contains("__Secure-3PSID");
    }

    private static bool IsYouTubeRelatedDomain(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain)) return false;
        var d = domain.TrimStart('.').ToLowerInvariant();
        if (d is "youtube.com" or "www.youtube.com" or "m.youtube.com"
            or "google.com" or "www.google.com" or "accounts.google.com"
            or "googleapis.com" or "ytimg.com" or "googlevideo.com")
            return true;

        return d.EndsWith(".youtube.com", StringComparison.Ordinal)
               || d.EndsWith(".google.com", StringComparison.Ordinal)
               || d.EndsWith(".googleapis.com", StringComparison.Ordinal);
    }
}

