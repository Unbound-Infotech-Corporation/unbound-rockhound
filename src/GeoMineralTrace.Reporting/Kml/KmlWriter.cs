using System.Globalization;
namespace GeoMineralTrace.Reporting.Kml;

/// <summary>In-memory KML document with toggleable folders (Google Earth layers).</summary>
public sealed class KmlDocument
{
    public required string Name { get; init; }
    public List<KmlFolder> Folders { get; } = [];
}

public sealed class KmlFolder
{
    public required string Name { get; init; }
    public bool Visible { get; init; } = true;
    public List<KmlPlacemark> Placemarks { get; } = [];
}

public sealed class KmlPlacemark
{
    public required string Name { get; init; }
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
    /// <summary>When set, renders a LineString instead of a Point.</summary>
    public IReadOnlyList<(double Longitude, double Latitude)>? LineCoordinates { get; init; }
    public string? DescriptionHtml { get; init; }
    public string? StyleId { get; init; }
}

/// <summary>Builds KML 2.2 XML suitable for Google Earth Pro / Web import.</summary>
public static class KmlWriter
{
    public static string Write(KmlDocument document)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(@"<?xml version=""1.0"" encoding=""UTF-8""?>");
        sb.AppendLine(@"<kml xmlns=""http://www.opengis.net/kml/2.2"">");
        sb.AppendLine("<Document>");
        sb.AppendLine($"  <name>{Escape(document.Name)}</name>");
        AppendStyles(sb);

        foreach (var folder in document.Folders)
        {
            sb.AppendLine("  <Folder>");
            sb.AppendLine($"    <name>{Escape(folder.Name)}</name>");
            sb.AppendLine($"    <visibility>{(folder.Visible ? 1 : 0)}</visibility>");
            foreach (var pm in folder.Placemarks)
                AppendPlacemark(sb, pm, indent: "    ");
            sb.AppendLine("  </Folder>");
        }

        sb.AppendLine("</Document>");
        sb.AppendLine("</kml>");
        return sb.ToString();
    }

    public static async Task<string> WriteToFileAsync(
        KmlDocument document,
        string path,
        CancellationToken cancellationToken = default)
    {
        var xml = Write(document);
        await File.WriteAllTextAsync(path, xml, System.Text.Encoding.UTF8, cancellationToken)
            .ConfigureAwait(false);
        return path;
    }

    private static void AppendStyles(System.Text.StringBuilder sb)
    {
        AppendStyle(sb, "locality", "ff22C55E");
        AppendStyle(sb, "claim-active", "ff5E6AD2");
        AppendStyle(sb, "claim-expiring", "ffCA8A04");
        AppendStyle(sb, "claim-lapsed", "ffA1A1AA");
        AppendStyle(sb, "find", "ffEC4899");
        AppendStyle(sb, "hypothesis", "ff5E6AD2");
        AppendStyle(sb, "locus", "ffF59E0B");
        AppendStyle(sb, "river", "ff3B82F6");
        AppendStyle(sb, "default", "ffE4E4E7");
    }

    private static void AppendStyle(System.Text.StringBuilder sb, string id, string abgr)
    {
        // KML color is aabbggrr
        sb.AppendLine($"  <Style id=\"{id}\">");
        sb.AppendLine("    <IconStyle>");
        sb.AppendLine($"      <color>{abgr}</color>");
        sb.AppendLine("      <scale>1.0</scale>");
        sb.AppendLine("      <Icon><href>http://maps.google.com/mapfiles/kml/paddle/wht-blank.png</href></Icon>");
        sb.AppendLine("    </IconStyle>");
        sb.AppendLine("  </Style>");
    }

    private static void AppendPlacemark(System.Text.StringBuilder sb, KmlPlacemark pm, string indent)
    {
        sb.AppendLine($"{indent}<Placemark>");
        sb.AppendLine($"{indent}  <name>{Escape(pm.Name)}</name>");
        if (!string.IsNullOrWhiteSpace(pm.StyleId))
            sb.AppendLine($"{indent}  <styleUrl>#{Escape(pm.StyleId)}</styleUrl>");
        if (!string.IsNullOrWhiteSpace(pm.DescriptionHtml))
        {
            sb.AppendLine($"{indent}  <description><![CDATA[");
            sb.AppendLine(pm.DescriptionHtml);
            sb.AppendLine($"{indent}  ]]></description>");
        }

        if (pm.LineCoordinates is { Count: >= 2 } line)
        {
            sb.AppendLine($"{indent}  <LineString>");
            sb.AppendLine($"{indent}    <tessellate>1</tessellate>");
            sb.AppendLine($"{indent}    <coordinates>");
            var coordText = string.Join(" ",
                line.Select(c =>
                    $"{c.Longitude.ToString(CultureInfo.InvariantCulture)},{c.Latitude.ToString(CultureInfo.InvariantCulture)},0"));
            sb.AppendLine($"{indent}      {coordText}");
            sb.AppendLine($"{indent}    </coordinates>");
            sb.AppendLine($"{indent}  </LineString>");
        }
        else
        {
            sb.AppendLine($"{indent}  <Point>");
            sb.AppendLine($"{indent}    <coordinates>{pm.Longitude.ToString(CultureInfo.InvariantCulture)},{pm.Latitude.ToString(CultureInfo.InvariantCulture)},0</coordinates>");
            sb.AppendLine($"{indent}  </Point>");
        }

        sb.AppendLine($"{indent}</Placemark>");
    }

    private static string Escape(string text) =>
        System.Security.SecurityElement.Escape(text) ?? text;
}
