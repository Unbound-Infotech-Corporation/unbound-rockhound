using GeoMineralTrace.Core.Common;
using GeoMineralTrace.Core.Evidence;
using GeoMineralTrace.Core.Hypothesis;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.Text;

namespace GeoMineralTrace_App.Helpers;

/// <summary>
/// WinUI presentation mapping for confidence-weighted UI (honest uncertainty).
/// </summary>
public static class ConfidenceVisual
{
    public sealed record Presentation(
        double RowOpacity,
        double DetailOpacity,
        FontWeight TitleWeight,
        FontWeight ConfidenceWeight,
        Brush TitleBrush,
        Brush DetailBrush,
        Brush ConfidenceBrush,
        double BorderOpacity,
        string? CorroborationNote);

    public static Presentation ForHypothesis(LocationHypothesis hypothesis) =>
        Build(
            ConfidenceProminence.FromHypothesis(hypothesis.Confidence, hypothesis.SupportingEvidenceIds.Count),
            hypothesis.SupportingEvidenceIds.Count < 2 ? "single-source · uncorroborated" : null);

    public static Presentation ForEvidence(EvidenceItem item) =>
        Build(
            ConfidenceProminence.FromEvidence(item),
            item.Confidence.Value < 0.4 && item.UserWeight == EvidenceWeight.Neutral
                ? "low confidence"
                : null);

    public static Presentation ForValue(Confidence confidence, int supportingSources = 1) =>
        Build(ConfidenceProminence.FromConfidence(confidence.Value, supportingSources), null);

    public static Presentation Build(double prominence, string? corroborationNote)
    {
        prominence = Math.Clamp(prominence, 0.0, 1.0);

        var rowOpacity = Lerp(0.42, 1.0, prominence);
        var detailOpacity = Lerp(0.35, 0.88, prominence);
        var borderOpacity = Lerp(0.25, 1.0, prominence);

        var titleWeight = prominence >= 0.72 ? FontWeights.Bold
            : prominence >= 0.48 ? FontWeights.SemiBold
            : FontWeights.Normal;

        var confidenceWeight = prominence >= 0.55 ? FontWeights.SemiBold : FontWeights.Normal;

        var titleBrush = BlendBrush("GmtBrushTextPrimary", "GmtBrushTextTertiary", prominence);
        var detailBrush = BlendBrush("GmtBrushTextSecondary", "GmtBrushTextDisabled", prominence * 0.85);
        var confidenceBrush = AccentBrush(prominence);

        return new Presentation(
            rowOpacity,
            detailOpacity,
            titleWeight,
            confidenceWeight,
            titleBrush,
            detailBrush,
            confidenceBrush,
            borderOpacity,
            corroborationNote);
    }

    private static Brush AccentBrush(double prominence)
    {
        var accent = prominence >= 0.68 ? ThemeColor("GmtConfidenceHigh")
            : prominence >= 0.38 ? ThemeColor("GmtConfidenceMedium")
            : ThemeColor("GmtConfidenceLow");

        var muted = ThemeColor("GmtColorTextTertiary");
        var blended = LerpColor(muted, accent, Lerp(0.35, 1.0, prominence));
        return new SolidColorBrush(blended);
    }

    private static Brush BlendBrush(string strongKey, string weakKey, double t)
    {
        var strong = ThemeColorFromBrush(strongKey);
        var weak = ThemeColorFromBrush(weakKey);
        return new SolidColorBrush(LerpColor(weak, strong, t));
    }

    private static Color ThemeColor(string key) =>
        (Color)Microsoft.UI.Xaml.Application.Current.Resources[key];

    private static Color ThemeColorFromBrush(string brushKey)
    {
        if (Microsoft.UI.Xaml.Application.Current.Resources[brushKey] is SolidColorBrush brush)
            return brush.Color;
        return ThemeColor(brushKey);
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * Math.Clamp(t, 0, 1);

    private static Color LerpColor(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            (byte)(a.A + (b.A - a.A) * t),
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }
}
