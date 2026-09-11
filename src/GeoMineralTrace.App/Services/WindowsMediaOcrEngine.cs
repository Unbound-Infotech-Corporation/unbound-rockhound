using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using GeoMineralTrace.Core.Common;
using GeoMineralTrace.Pipeline.Abstractions;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using PipelineOcrResult = GeoMineralTrace.Pipeline.Abstractions.OcrResult;

namespace GeoMineralTrace_App.Services;

/// <summary>
/// Real OCR via Windows.Media.Ocr for keyframe bitmaps.
/// </summary>
public sealed class WindowsMediaOcrEngine : IOcrEngine
{
    private static readonly TimeSpan PerImageTimeout = TimeSpan.FromSeconds(20);

    public string EngineName => "Windows.Media.Ocr";

    public async Task<IReadOnlyList<PipelineOcrResult>> RecognizeAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return [];

        var ext = Path.GetExtension(imagePath);
        if (!ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".png", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase))
            return [];

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(PerImageTimeout);

        try
        {
            return await RecognizeCoreAsync(imagePath, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Per-image timeout — skip this frame rather than hanging the whole pipeline.
            return [];
        }
        catch
        {
            return [];
        }
    }

    private static async Task<IReadOnlyList<PipelineOcrResult>> RecognizeCoreAsync(
        string imagePath,
        CancellationToken cancellationToken)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
                     ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en"));
        if (engine is null)
            return [];

        // Avoid StorageFile.GetFileFromPathAsync — it can hang from Task.Run / MTA for some paths.
        var bytes = await File.ReadAllBytesAsync(imagePath, cancellationToken).ConfigureAwait(false);
        using var mem = new InMemoryRandomAccessStream();
        await mem.WriteAsync(bytes.AsBuffer());
        mem.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(mem).AsTask(cancellationToken)
            .ConfigureAwait(false);
        var bitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);

        if (bitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8 ||
            bitmap.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
        {
            bitmap = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var result = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken)
            .ConfigureAwait(false);
        if (result is null || string.IsNullOrWhiteSpace(result.Text))
            return [];

        var sb = new StringBuilder();
        foreach (var line in result.Lines)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.Append(line.Text);
        }

        var text = sb.ToString().Trim();
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var confidence = text.Length >= 8 ? Confidence.High
            : text.Length >= 3 ? Confidence.Medium
            : Confidence.Low;

        return
        [
            new PipelineOcrResult(
                text,
                confidence,
                engine.RecognizerLanguage.LanguageTag,
                "Recognized with Windows.Media.Ocr")
        ];
    }
}
