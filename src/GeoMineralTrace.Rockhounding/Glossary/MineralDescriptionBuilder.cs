using System.Text;

namespace GeoMineralTrace.Rockhounding.Glossary;

/// <summary>
/// Licensing-safe description builder: generates prose from structured facts only.
/// Does not paraphrase Mindat / Webmineral / copyrighted site text.
/// </summary>
public static class MineralDescriptionBuilder
{
    public static string Build(
        string name,
        string? formula,
        string? crystalSystem,
        double? mohsMin,
        double? mohsMax,
        string? colorRange,
        string? luster,
        string? imaStatus,
        int? imaYear)
    {
        var sb = new StringBuilder();
        sb.Append(name.Trim());

        if (!string.IsNullOrWhiteSpace(imaStatus))
        {
            sb.Append(" is catalogued as ");
            sb.Append(imaStatus.Trim());
            if (imaYear is > 1800 and < 2100)
                sb.Append(" (IMA-related year ").Append(imaYear.Value).Append(')');
            sb.Append('.');
        }
        else
        {
            sb.Append('.');
        }

        if (!string.IsNullOrWhiteSpace(formula))
            sb.Append(" Chemical formula: ").Append(formula.Trim()).Append('.');

        if (!string.IsNullOrWhiteSpace(crystalSystem))
            sb.Append(" Crystal system: ").Append(crystalSystem.Trim()).Append('.');

        if (mohsMin is not null || mohsMax is not null)
        {
            sb.Append(" Mohs hardness");
            if (mohsMin is not null && mohsMax is not null && Math.Abs(mohsMin.Value - mohsMax.Value) > 0.05)
                sb.Append(" range ").Append(FormatMohs(mohsMin.Value)).Append('–').Append(FormatMohs(mohsMax.Value));
            else
                sb.Append(' ').Append(FormatMohs(mohsMin ?? mohsMax!.Value));
            sb.Append('.');
        }

        if (!string.IsNullOrWhiteSpace(colorRange))
            sb.Append(" Typical color range: ").Append(colorRange.Trim()).Append('.');

        if (!string.IsNullOrWhiteSpace(luster))
            sb.Append(" Luster: ").Append(luster.Trim()).Append('.');

        sb.Append(" Facts for this entry come from structured open sources (Wikidata / IMA list where available); ");
        sb.Append("the description is generated from those fields, not copied from copyrighted mineral databases.");
        return sb.ToString();
    }

    private static string FormatMohs(double v) =>
        Math.Abs(v - Math.Round(v)) < 0.05 ? ((int)Math.Round(v)).ToString() : v.ToString("0.#");
}
