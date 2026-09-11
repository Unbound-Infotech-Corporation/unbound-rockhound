using System.Reflection;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GeoMineralTrace_App.Helpers;

/// <summary>
/// Makes enabled/disabled action affordances unambiguous: tooltip reason + not-allowed cursor.
/// </summary>
internal static class ActionAvailability
{
    // UIElement.ProtectedCursor is protected; set via reflection so call sites stay simple.
    private static readonly PropertyInfo? ProtectedCursorProperty =
        typeof(UIElement).GetProperty(
            "ProtectedCursor",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    public static void Set(
        Button button,
        bool enabled,
        string enabledTooltip,
        string disabledReason)
    {
        button.IsEnabled = enabled;
        ToolTipService.SetToolTip(button, enabled ? enabledTooltip : disabledReason);
        ApplyCursor(button, enabled);
    }

    public static void ApplyCursor(UIElement element, bool enabled)
    {
        try
        {
            if (ProtectedCursorProperty is null)
                return;

            var cursor = InputSystemCursor.Create(
                enabled ? InputSystemCursorShape.Arrow : InputSystemCursorShape.UniversalNo);
            ProtectedCursorProperty.SetValue(element, cursor);
        }
        catch
        {
            // Cursor is best-effort; tooltip + disabled VisualState remain the primary signal.
        }
    }
}
