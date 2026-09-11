using System.Globalization;
using System.Text;
using GeoMineralTrace.Core.App;

namespace GeoMineralTrace.Pipeline.Ingest;

/// <summary>
/// Local Netscape cookie jar for authenticated Instagram sessions (private / login-required posts).
/// </summary>
public static class InstagramSessionStore
{
    public static string DataDirectory => AppDataPaths.LocalRoot;

    public static string CookieFilePath => Path.Combine(DataDirectory, "instagram-cookies.txt");

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
        sb.AppendLine("# GeoMineral Trace — Instagram session (local only). Sign out to delete.");
        sb.AppendLine();

        var written = 0;
        foreach (var c in cookies)
        {
            if (string.IsNullOrWhiteSpace(c.Domain) || string.IsNullOrWhiteSpace(c.Name))
                continue;

            var domain = c.Domain.StartsWith('.') ? c.Domain : "." + c.Domain.TrimStart('.');
            if (!IsInstagramRelatedDomain(domain))
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
            throw new InvalidOperationException("No Instagram cookies were captured. Sign in fully, then click Save session.");

        File.WriteAllText(CookieFilePath, sb.ToString(), Encoding.UTF8);
    }

    public static bool LooksSignedIn(IEnumerable<SocialMediaCookie> cookies)
    {
        var names = cookies
            .Where(c => IsInstagramRelatedDomain(c.Domain))
            .Select(c => c.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return names.Contains("sessionid")
               || names.Contains("ds_user_id")
               || names.Contains("csrftoken");
    }

    private static bool IsInstagramRelatedDomain(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain)) return false;
        var d = domain.TrimStart('.').ToLowerInvariant();
        if (d is "instagram.com" or "www.instagram.com")
            return true;

        return d.EndsWith(".instagram.com", StringComparison.Ordinal);
    }
}
