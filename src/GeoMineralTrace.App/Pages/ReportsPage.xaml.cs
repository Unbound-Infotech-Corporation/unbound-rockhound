using System.Diagnostics;
using GeoMineralTrace.Core.App;
using GeoMineralTrace.Infrastructure.Services;
using GeoMineralTrace.Reporting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace GeoMineralTrace_App.Pages;

public sealed partial class ReportsPage : Page
{
    private static string ReportsDir => AppDataPaths.Sub("Reports");

    private bool _emptyActionWired;

    public ReportsPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (_emptyActionWired)
                return;
            ReportEmpty.ActionLabel = "Go to Analyze";
            ReportEmpty.ActionClick += (_, _) => MainWindow.Instance?.NavigateTo(typeof(AnalysisPage), "analysis");
            _emptyActionWired = true;
        };
        Refresh_Click(this, new RoutedEventArgs());
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var session = App.Services.GetRequiredService<AnalysisSessionContext>();
            var result = session.LastPipelineResult ?? AnalysisPage.LastResult;
            if (result is null)
            {
                await new ContentDialog
                {
                    Title = "No analysis",
                    Content = "Run an analysis first (Analyze page), or build a solar locus / refuse evidence so hypotheses are available.",
                    CloseButtonText = "OK",
                    XamlRoot = XamlRoot
                }.ShowAsync();
                return;
            }

            AnalysisPage.LastResult = result;
            var exporter = App.Services.GetRequiredService<ResearchReportExporter>();
            var paths = await exporter.ExportAsync(result, ReportsDir);
            Refresh_Click(sender, e);
            await new ContentDialog
            {
                Title = "Exported",
                Content = paths.MarkdownPath,
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

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(ReportsDir);
        Process.Start(new ProcessStartInfo
        {
            FileName = ReportsDir,
            UseShellExecute = true
        });
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(ReportsDir);
        var files = Directory.GetFiles(ReportsDir, "report-*.md")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Select(Path.GetFileName)
            .Where(n => n is not null)
            .Cast<string>()
            .ToList();
        ReportList.ItemsSource = files;
        ReportEmpty.Visibility = files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ReportList.Visibility = files.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ReportList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (ReportList.SelectedItem is not string name) return;
        var path = Path.Combine(ReportsDir, name);
        if (File.Exists(path))
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }
}