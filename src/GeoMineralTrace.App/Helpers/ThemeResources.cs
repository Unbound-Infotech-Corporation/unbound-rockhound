using Microsoft.UI.Xaml;
using Windows.UI;

namespace GeoMineralTrace_App.Helpers;

internal static class ThemeResources
{
    public static Color GetColor(string key, ElementTheme? theme = null)
    {
        var themeKey = ResolveThemeKey(theme);
        if (Application.Current.Resources.ThemeDictionaries.TryGetValue(themeKey, out var themeDictObj)
            && themeDictObj is ResourceDictionary themeDict
            && themeDict.TryGetValue(key, out var value)
            && value is Color color)
        {
            return color;
        }

        if (Application.Current.Resources.TryGetValue(key, out var direct) && direct is Color c)
            return c;

        return Color.FromArgb(255, 255, 255, 255);
    }

    public static double GetDouble(string key)
    {
        if (Application.Current.Resources.TryGetValue(key, out var value) && value is double d)
            return d;
        return 0;
    }

    private static string ResolveThemeKey(ElementTheme? theme)
    {
        if (theme is ElementTheme.Light)
            return "Light";
        if (theme is ElementTheme.Dark)
            return "Dark";

        return Application.Current.RequestedTheme == ApplicationTheme.Light ? "Light" : "Dark";
    }
}
