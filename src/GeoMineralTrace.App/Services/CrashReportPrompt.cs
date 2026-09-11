using GeoMineralTrace_App.Dialogs;
using GeoMineralTrace_App.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace GeoMineralTrace_App.Services;

/// <summary>Shows the crash-report dialog on the UI thread when an error is captured.</summary>
public static class CrashReportPrompt
{
    public static void TryShow(string reportId, string? reportPath = null)
    {
        var window = MainWindow.Instance;
        var queue = window?.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
        if (queue is null)
            return;

        queue.TryEnqueue(async () =>
        {
            try
            {
                if (window?.Content?.XamlRoot is null)
                    return;

                var dialog = new CrashReportDialog(reportId, reportPath)
                {
                    XamlRoot = window.Content.XamlRoot
                };
                await dialog.ShowAsync();
                DiagnosticReportCenter.ClearPendingCrash();
            }
            catch (Exception ex)
            {
                DiagnosticReportCenter.Log($"CrashReportPrompt failed: {ex.Message}");
            }
        });
    }

    public static void TryShowPendingFromPreviousSession()
    {
        var pending = DiagnosticReportCenter.TryGetPendingCrash();
        if (pending is null)
            return;

        DiagnosticReportCenter.Log(
            $"Previous session crash detected ({pending.ReportId}): {pending.Message}");
        TryShow(pending.ReportId, pending.ReportPath);
    }
}
