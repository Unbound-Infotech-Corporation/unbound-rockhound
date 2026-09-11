using System.Diagnostics;
using GeoMineralTrace_App.Dialogs;
using GeoMineralTrace_App.Helpers;
using GeoMineralTrace_App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace GeoMineralTrace_App.Dialogs;

public sealed partial class CrashReportDialog : ContentDialog
{
    private readonly string _reportPath;
    private readonly string _reportId;

    public CrashReportDialog(string reportId, string? reportPath = null)
    {
        InitializeComponent();
        _reportId = reportId;
        _reportPath = reportPath ?? DiagnosticReportCenter.FindReportPath(reportId)
            ?? Path.Combine(DiagnosticReportCenter.ReportsDirectory, $"report-unknown-{_reportId}.txt");

        PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            await CopyReportAsync();
        };
        SecondaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            await SaveReportAsync();
        };

        var preview = File.Exists(_reportPath)
            ? File.ReadAllText(_reportPath)
            : "Report file could not be located.";
        var firstLine = preview.Split('\n').FirstOrDefault()?.Trim() ?? "Diagnostic report";
        SummaryText.Text = $"Report ID: {_reportId}\n{firstLine}\n\n{Truncate(preview, 400)}";
    }

    private async Task CopyReportAsync()
    {
        var text = BuildFinalReportText();
        var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
        data.SetText(text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
        Windows.ApplicationModel.DataTransfer.Clipboard.Flush();

        await new ContentDialog
        {
            Title = "Copied",
            Content = "The full diagnostic report is on your clipboard. Paste it into email or a support ticket.",
            CloseButtonText = "OK",
            XamlRoot = XamlRoot
        }.ShowAsync();
    }

    private async void SaveZip_Click(object sender, RoutedEventArgs e) =>
        await SaveZipBundleAsync();

    private async void SendEmail_Click(object sender, RoutedEventArgs e)
    {
        var notes = UserNotesBox.Text;
        if (!string.IsNullOrWhiteSpace(notes))
            DiagnosticReportCenter.AppendUserNotesToReport(_reportPath, notes);

        try
        {
            var uri = DiagnosticReportCenter.BuildMailtoUri(_reportPath, notes);
            Process.Start(new ProcessStartInfo { FileName = uri, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            await new ContentDialog
            {
                Title = "Could not open email",
                Content = ex.Message + "\n\nUse Copy report or Save zip bundle instead.",
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            }.ShowAsync();
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(DiagnosticReportCenter.ReportsDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = DiagnosticReportCenter.ReportsDirectory,
            UseShellExecute = true
        });
    }

    private async Task SaveReportAsync()
    {
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindowInstance));
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add("Diagnostic report", [".txt"]);
        picker.SuggestedFileName = Path.GetFileName(_reportPath);

        var file = await picker.PickSaveFileAsync();
        if (file is null) return;

        await File.WriteAllTextAsync(file.Path, BuildFinalReportText());
    }

    private async Task SaveZipBundleAsync()
    {
        var notes = UserNotesBox.Text;
        if (!string.IsNullOrWhiteSpace(notes))
            DiagnosticReportCenter.AppendUserNotesToReport(_reportPath, notes);

        try
        {
            var zip = await DiagnosticReportCenter.ExportReportBundleAsync(_reportPath);
            await new ContentDialog
            {
                Title = "Bundle saved",
                Content = $"Saved:\n{zip}\n\nAttach this zip when contacting support.",
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            }.ShowAsync();
        }
        catch (Exception ex)
        {
            await new ContentDialog
            {
                Title = "Export failed",
                Content = ex.Message,
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            }.ShowAsync();
        }
    }

    private string BuildFinalReportText()
    {
        var notes = UserNotesBox.Text;
        if (!string.IsNullOrWhiteSpace(notes))
            return DiagnosticReportCenter.AppendUserNotesToReport(_reportPath, notes);
        return File.Exists(_reportPath) ? File.ReadAllText(_reportPath) : "";
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}
