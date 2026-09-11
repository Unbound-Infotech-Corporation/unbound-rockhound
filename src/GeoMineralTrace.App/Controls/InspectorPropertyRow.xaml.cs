using GeoMineralTrace.Core.Common;
using GeoMineralTrace_App.Helpers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Text;

namespace GeoMineralTrace_App.Controls;

public sealed partial class InspectorPropertyRow : UserControl
{
    public InspectorPropertyRow()
    {
        InitializeComponent();
    }

    public string Label
    {
        get => LabelText.Text;
        set => LabelText.Text = value.ToUpperInvariant();
    }

    public string Value
    {
        get => ValueText.Text;
        set => ValueText.Text = value;
    }

    /// <summary>Scale value text contrast/weight by confidence prominence.</summary>
    public void ApplyConfidence(Confidence confidence, int supportingSources = 1)
    {
        var visual = ConfidenceVisual.ForValue(confidence, supportingSources);
        ValueText.Foreground = visual.ConfidenceBrush;
        ValueText.FontWeight = visual.ConfidenceWeight;
        ValueText.Opacity = visual.RowOpacity;
    }

    public void ResetValueStyle()
    {
        ValueText.ClearValue(TextBlock.ForegroundProperty);
        ValueText.ClearValue(TextBlock.FontWeightProperty);
        ValueText.Opacity = 1;
    }
}
