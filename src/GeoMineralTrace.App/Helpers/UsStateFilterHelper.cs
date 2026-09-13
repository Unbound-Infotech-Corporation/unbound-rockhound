using GeoMineralTrace.Core.Geo;
using Microsoft.UI.Xaml.Controls;

namespace GeoMineralTrace_App.Helpers;

/// <summary>Shared state filter ComboBox helpers for Rivers / Claims / Rockhounding toolbars.</summary>
public static class UsStateFilterHelper
{
    public const double FilterWidth = 220;

    /// <summary>Populate a ComboBox with All + 50 states. Selects All if nothing selected.</summary>
    public static void Populate(ComboBox combo)
    {
        if (combo.Items.Count > 0)
            return;

        combo.PlaceholderText = "All states";
        combo.Items.Add(new ComboBoxItem { Content = "All states", Tag = "" });
        foreach (var place in UsPlaceCentroids.All)
        {
            combo.Items.Add(new ComboBoxItem
            {
                Content = place.Display,
                Tag = place.Code
            });
        }

        combo.SelectedIndex = 0;
    }

    /// <summary>
    /// Resolves a 2-letter state code from a ComboBox selection and/or free text.
    /// Accepts "WA", "Washington", "Washington (WA)". Returns null for All / empty.
    /// </summary>
    public static string? ResolveStateCode(ComboBox? combo, string? freeText = null)
    {
        if (combo?.SelectedItem is ComboBoxItem { Tag: string code } && !string.IsNullOrWhiteSpace(code))
            return UsStateCode.Normalize(code) ?? code.Trim().ToUpperInvariant();

        if (combo?.SelectedItem is ComboBoxItem { Content: string display })
        {
            var fromDisplay = UsStateCode.Normalize(display);
            if (fromDisplay is not null)
                return fromDisplay;
        }

        return NormalizeStateCode(freeText);
    }

    public static string? NormalizeStateCode(string? raw) => UsStateCode.Normalize(raw);
}
