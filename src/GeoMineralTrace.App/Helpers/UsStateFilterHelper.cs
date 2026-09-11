using Microsoft.UI.Xaml.Controls;

namespace GeoMineralTrace_App.Helpers;

/// <summary>Shared state filter ComboBox helpers for Rivers / Claims / Rockhounding toolbars.</summary>
public static class UsStateFilterHelper
{
    /// <summary>Populate a ComboBox with All + 50 states. Selects All if nothing selected.</summary>
    public static void Populate(ComboBox combo)
    {
        if (combo.Items.Count > 0)
            return;

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
            return code.Trim().ToUpperInvariant();

        return NormalizeStateCode(freeText);
    }

    public static string? NormalizeStateCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var t = raw.Trim();
        if (t.Length == 2)
            return t.ToUpperInvariant();

        // "Washington (WA)" from Display
        var open = t.LastIndexOf('(');
        var close = t.LastIndexOf(')');
        if (open >= 0 && close > open + 1)
        {
            var inside = t[(open + 1)..close].Trim();
            if (inside.Length == 2)
                return inside.ToUpperInvariant();
        }

        var byName = UsPlaceCentroids.All.FirstOrDefault(p =>
            string.Equals(p.Name, t, StringComparison.OrdinalIgnoreCase)
            || string.Equals(p.Display, t, StringComparison.OrdinalIgnoreCase));
        return byName?.Code;
    }
}
