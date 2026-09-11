using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GeoMineralTrace_App.Controls;

public sealed partial class EmptyState : UserControl
{
    public EmptyState()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => TitleText.Text;
        set => TitleText.Text = value;
    }

    public string Description
    {
        get => DescriptionText.Text;
        set => DescriptionText.Text = value;
    }

    public string Icon
    {
        get => StateIcon.Glyph;
        set => StateIcon.Glyph = value;
    }

    public string? ActionLabel
    {
        get => ActionButton.Content?.ToString();
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                ActionButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                ActionButton.Content = value;
                ActionButton.Visibility = Visibility.Visible;
            }
        }
    }

    public event RoutedEventHandler? ActionClick;

    private void ActionButton_Click(object sender, RoutedEventArgs e) =>
        ActionClick?.Invoke(this, e);
}
