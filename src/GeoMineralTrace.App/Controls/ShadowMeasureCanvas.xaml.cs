using GeoMineralTrace_App.Helpers;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace GeoMineralTrace_App.Controls;

public sealed partial class ShadowMeasureCanvas : UserControl
{
    private Point? _objectTop;
    private Point? _objectBase;
    private Point? _shadowTip;
    private BitmapImage? _bitmap;
    private int _clickStep;

    public ShadowMeasureCanvas()
    {
        InitializeComponent();
        OverlayCanvas.PointerPressed += OverlayCanvas_PointerPressed;
        OverlayCanvas.SizeChanged += (_, _) => RedrawOverlay();
        HostGrid.SizeChanged += (_, _) => RedrawOverlay();
        ActualThemeChanged += (_, _) => RedrawOverlay();
    }

    public event EventHandler? MeasurementChanged;

    public double ObjectHeightPixels { get; private set; }
    public double ShadowLengthPixels { get; private set; }
    public double? ShadowAzimuthDegrees { get; private set; }
    public bool HasCompleteMeasurement => _objectTop is not null && _objectBase is not null && _shadowTip is not null;

    public async Task LoadImageAsync(string? imagePath)
    {
        ResetMeasurement();
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            KeyframeImage.Source = null;
            _bitmap = null;
            InstructionText.Text = "No keyframe image available. Run analysis with ffmpeg, or pick a local image.";
            return;
        }

        _bitmap = new BitmapImage();
        _bitmap.ImageOpened += (_, _) => RedrawOverlay();
        _bitmap.UriSource = new Uri(imagePath);
        KeyframeImage.Source = _bitmap;
        InstructionText.Text = "Click (1) object top, (2) object base, (3) shadow tip. Ratio h/L drives elevation.";
        await Task.CompletedTask;
    }

    public void ResetMeasurement()
    {
        _objectTop = null;
        _objectBase = null;
        _shadowTip = null;
        _clickStep = 0;
        ObjectHeightPixels = 0;
        ShadowLengthPixels = 0;
        ShadowAzimuthDegrees = null;
        OverlayCanvas.Children.Clear();
        MetricsText.Text = "";
        MeasurementChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e) => ResetMeasurement();

    private void OverlayCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_bitmap is null)
            return;

        var pos = e.GetCurrentPoint(OverlayCanvas).Position;
        if (!TryMapToImageSpace(pos, out var imagePoint))
            return;

        switch (_clickStep)
        {
            case 0:
                _objectTop = imagePoint;
                _clickStep = 1;
                InstructionText.Text = "Step 2/3: click the object base (ground contact).";
                break;
            case 1:
                _objectBase = imagePoint;
                _clickStep = 2;
                InstructionText.Text = "Step 3/3: click the shadow tip away from the object.";
                break;
            default:
                _shadowTip = imagePoint;
                _clickStep = 3;
                InstructionText.Text = "Measurement complete. Adjust numbers or add to trajectory.";
                break;
        }

        UpdateMetrics();
        RedrawOverlay();
        MeasurementChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateMetrics()
    {
        if (_objectTop is not { } top || _objectBase is not { } bas)
        {
            MetricsText.Text = _clickStep > 0 ? "Base point pending…" : "";
            return;
        }

        ObjectHeightPixels = Distance(top, bas);
        MetricsText.Text = $"h = {ObjectHeightPixels:F1} px";

        if (_shadowTip is not { } tip)
            return;

        ShadowLengthPixels = Distance(bas, tip);
        var dx = tip.X - bas.X;
        var dy = tip.Y - bas.Y;
        // Screen-space azimuth: 0° = right, clockwise (image coords). Geographic azimuth needs camera bearing.
        var screenAz = Math.Atan2(dx, -dy) * 180.0 / Math.PI;
        if (screenAz < 0)
            screenAz += 360;
        ShadowAzimuthDegrees = screenAz;

        MetricsText.Text =
            $"h = {ObjectHeightPixels:F1} px · L = {ShadowLengthPixels:F1} px · ratio = {(ObjectHeightPixels / Math.Max(ShadowLengthPixels, 0.001)):F3} · screen az ≈ {ShadowAzimuthDegrees:F0}°";
    }

    private void RedrawOverlay()
    {
        OverlayCanvas.Children.Clear();
        if (_bitmap is null)
            return;

        DrawPoint(_objectTop, ThemeResources.GetColor("GmtOverlayObjectHeight", ActualTheme), "top");
        DrawPoint(_objectBase, ThemeResources.GetColor("GmtAccentGeo", ActualTheme), "base");
        DrawPoint(_shadowTip, ThemeResources.GetColor("GmtOverlayShadowVector", ActualTheme), "tip");

        if (_objectTop is { } top && _objectBase is { } bas)
            DrawLine(top, bas, ThemeResources.GetColor("GmtOverlayObjectHeight", ActualTheme), ThemeResources.GetDouble("GmtOverlayLineEmphasis"));

        if (_objectBase is { } b && _shadowTip is { } tip)
            DrawLine(b, tip, ThemeResources.GetColor("GmtOverlayShadowVector", ActualTheme), ThemeResources.GetDouble("GmtOverlayLineEmphasis"));
    }

    private void DrawPoint(Point? imagePoint, Color color, string label)
    {
        if (imagePoint is not { } p || !TryMapToCanvasSpace(p, out var canvasPoint))
            return;

        var handleSize = ThemeResources.GetDouble("GmtOverlayHandleSize");
        var half = handleSize / 2;
        var ellipse = new Ellipse
        {
            Width = handleSize,
            Height = handleSize,
            Fill = new SolidColorBrush(color),
            Stroke = new SolidColorBrush(ThemeResources.GetColor("GmtOverlayHandle", ActualTheme)),
            StrokeThickness = 1
        };
        Canvas.SetLeft(ellipse, canvasPoint.X - half);
        Canvas.SetTop(ellipse, canvasPoint.Y - half);
        OverlayCanvas.Children.Add(ellipse);

        var tb = new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(color),
            FontFamily = (FontFamily)Application.Current.Resources["GmtFontMono"],
            FontSize = (double)Application.Current.Resources["GmtFontSizeCaption"]
        };
        Canvas.SetLeft(tb, canvasPoint.X + 6);
        Canvas.SetTop(tb, canvasPoint.Y - 6);
        OverlayCanvas.Children.Add(tb);
    }

    private void DrawLine(Point a, Point b, Color color, double thickness)
    {
        if (!TryMapToCanvasSpace(a, out var ca) || !TryMapToCanvasSpace(b, out var cb))
            return;

        OverlayCanvas.Children.Add(new Line
        {
            X1 = ca.X,
            Y1 = ca.Y,
            X2 = cb.X,
            Y2 = cb.Y,
            Stroke = new SolidColorBrush(color),
            StrokeThickness = thickness
        });
    }

    private bool TryMapToImageSpace(Point canvasPoint, out Point imagePoint)
    {
        imagePoint = default;
        if (_bitmap is null || OverlayCanvas.ActualWidth <= 0 || OverlayCanvas.ActualHeight <= 0)
            return false;

        var nw = _bitmap.PixelWidth;
        var nh = _bitmap.PixelHeight;
        if (nw <= 0 || nh <= 0)
            return false;

        var cw = OverlayCanvas.ActualWidth;
        var ch = OverlayCanvas.ActualHeight;
        var scale = Math.Min(cw / nw, ch / nh);
        var rw = nw * scale;
        var rh = nh * scale;
        var ox = (cw - rw) / 2;
        var oy = (ch - rh) / 2;

        if (canvasPoint.X < ox || canvasPoint.Y < oy || canvasPoint.X > ox + rw || canvasPoint.Y > oy + rh)
            return false;

        imagePoint = new Point((canvasPoint.X - ox) / scale, (canvasPoint.Y - oy) / scale);
        return true;
    }

    private bool TryMapToCanvasSpace(Point imagePoint, out Point canvasPoint)
    {
        canvasPoint = default;
        if (_bitmap is null || OverlayCanvas.ActualWidth <= 0 || OverlayCanvas.ActualHeight <= 0)
            return false;

        var nw = _bitmap.PixelWidth;
        var nh = _bitmap.PixelHeight;
        if (nw <= 0 || nh <= 0)
            return false;

        var cw = OverlayCanvas.ActualWidth;
        var ch = OverlayCanvas.ActualHeight;
        var scale = Math.Min(cw / nw, ch / nh);
        var rw = nw * scale;
        var rh = nh * scale;
        var ox = (cw - rw) / 2;
        var oy = (ch - rh) / 2;

        canvasPoint = new Point(ox + imagePoint.X * scale, oy + imagePoint.Y * scale);
        return true;
    }

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
