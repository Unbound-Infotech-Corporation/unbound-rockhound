using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using GeoMineralTrace_App.Helpers;
using Windows.UI;

namespace GeoMineralTrace_App.Controls;

public sealed partial class AnalysisStagePanel : UserControl
{
    private readonly (string Id, string Label)[] _stages =
    [
        ("metadata", "Extracting metadata"),
        ("keyframes", "Detecting keyframes"),
        ("ocr", "Running OCR"),
        ("audio", "Transcribing audio"),
        ("vision", "Tagging scenes"),
        ("fusion", "Fusing hypotheses"),
        ("rockhounding", "Cross-linking localities"),
    ];

    private readonly Dictionary<string, (Border Row, FontIcon Icon, TextBlock Label)> _rows = new(StringComparer.OrdinalIgnoreCase);

    public AnalysisStagePanel()
    {
        InitializeComponent();
        Loaded += (_, _) => BuildRows();
        ActualThemeChanged += (_, _) => RefreshColors();
    }

    public void Reset()
    {
        foreach (var (_, row) in _rows)
            SetRowState(row.Row, row.Icon, row.Label, "pending");
    }

    public void ApplyStage(string stageId, bool isComplete = false)
    {
        if (!_rows.TryGetValue(stageId, out var current))
            return;

        var index = Array.FindIndex(_stages, s => s.Id.Equals(stageId, StringComparison.OrdinalIgnoreCase));
        for (var i = 0; i < index; i++)
        {
            var id = _stages[i].Id;
            if (_rows.TryGetValue(id, out var prev))
                SetRowState(prev.Row, prev.Icon, prev.Label, "complete");
        }

        SetRowState(current.Row, current.Icon, current.Label, isComplete ? "complete" : "active");

        for (var i = index + 1; i < _stages.Length; i++)
        {
            var id = _stages[i].Id;
            if (_rows.TryGetValue(id, out var next))
                SetRowState(next.Row, next.Icon, next.Label, "pending");
        }
    }

    public void MarkAllComplete()
    {
        foreach (var (_, row) in _rows)
            SetRowState(row.Row, row.Icon, row.Label, "complete");
    }

    private void BuildRows()
    {
        StageList.Children.Clear();
        _rows.Clear();

        foreach (var (id, label) in _stages)
        {
            var icon = new FontIcon
            {
                FontSize = 12,
                Glyph = "\uE73E",
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            var text = new TextBlock
            {
                Text = label,
                Style = (Style)Application.Current.Resources["GmtTextBodySm"],
                VerticalAlignment = VerticalAlignment.Center
            };

            var grid = new Grid { ColumnSpacing = 0 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(icon, 0);
            Grid.SetColumn(text, 1);
            grid.Children.Add(icon);
            grid.Children.Add(text);

            var row = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 6, 8, 6),
                Child = grid
            };

            StageList.Children.Add(row);
            _rows[id] = (row, icon, text);
            SetRowState(row, icon, text, "pending");
        }
    }

    private void RefreshColors()
    {
        foreach (var (id, _) in _stages)
        {
            if (!_rows.TryGetValue(id, out var row))
                continue;
            // Re-apply based on current glyph/state — simplified: pending unless icon shows check
            var state = row.Icon.Glyph == "\uE73E" ? "complete"
                : row.Icon.Glyph == "\uE768" ? "active"
                : "pending";
            SetRowState(row.Row, row.Icon, row.Label, state);
        }
    }

    private void SetRowState(Border row, FontIcon icon, TextBlock label, string state)
    {
        Color fg;
        Color bg = Colors.Transparent;
        string glyph;

        switch (state)
        {
            case "complete":
                fg = ThemeResources.GetColor("GmtStageComplete", ActualTheme);
                glyph = "\uE73E";
                break;
            case "active":
                fg = ThemeResources.GetColor("GmtStageActive", ActualTheme);
                bg = ThemeResources.GetColor("GmtColorPrimarySubtle", ActualTheme);
                glyph = "\uE768";
                break;
            default:
                fg = ThemeResources.GetColor("GmtStagePending", ActualTheme);
                glyph = "\uE915";
                break;
        }

        icon.Foreground = new SolidColorBrush(fg);
        icon.Glyph = glyph;
        label.Foreground = new SolidColorBrush(fg);
        row.Background = new SolidColorBrush(bg);
    }
}
