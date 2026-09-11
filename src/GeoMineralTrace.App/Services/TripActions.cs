using GeoMineralTrace.Core.Hypothesis;
using GeoMineralTrace.Core.Trips;
using GeoMineralTrace.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GeoMineralTrace_App.Services;

public static class TripActions
{
    public static async Task<bool> AddHypothesisToTripAsync(
        XamlRoot xamlRoot,
        LocationHypothesis hypothesis,
        Guid? sessionId,
        string? videoTitle)
    {
        if (hypothesis.Center is null)
        {
            await ShowMessageAsync(xamlRoot, "No coordinates", "This hypothesis has no map coordinates yet.");
            return false;
        }

        var store = App.Services.GetRequiredService<TripStore>();
        var trips = await store.ListTripsAsync();
        Trip? targetTrip = null;
        var siteType = SiteType.DigSite;

        if (trips.Count == 0)
        {
            var nameBox = new TextBox { PlaceholderText = "Trip name", Text = "Field trip" };
            var descBox = new TextBox { PlaceholderText = "Optional notes", AcceptsReturn = true, Height = 72 };
            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(nameBox);
            panel.Children.Add(descBox);
            var create = await new ContentDialog
            {
                Title = "Create your first trip",
                Content = panel,
                PrimaryButtonText = "Create & add",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = xamlRoot
            }.ShowAsync();
            if (create != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(nameBox.Text))
                return false;
            targetTrip = await store.CreateTripAsync(nameBox.Text, descBox.Text);
        }
        else
        {
            var tripNames = trips.Select(t => t.Name).ToList();
            tripNames.Add("+ New trip…");
            var picker = new ComboBox { ItemsSource = tripNames, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
            var sitePicker = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                SelectedIndex = 0,
                ItemsSource = new[] { "Dig site", "River bank", "Quarry", "Claim" }
            };
            var panel = new StackPanel { Spacing = 8, MinWidth = 320 };
            panel.Children.Add(new TextBlock { Text = $"Add “{hypothesis.Label}” as a waypoint", TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(picker);
            panel.Children.Add(sitePicker);
            var dialog = await new ContentDialog
            {
                Title = "Add to trip",
                Content = panel,
                PrimaryButtonText = "Add waypoint",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = xamlRoot
            }.ShowAsync();
            if (dialog != ContentDialogResult.Primary)
                return false;

            siteType = sitePicker.SelectedIndex switch
            {
                1 => SiteType.RiverBank,
                2 => SiteType.Quarry,
                3 => SiteType.Claim,
                _ => SiteType.DigSite
            };

            if (picker.SelectedIndex == tripNames.Count - 1)
            {
                var nameBox = new TextBox { PlaceholderText = "New trip name", Text = hypothesis.Label };
                var create = await new ContentDialog
                {
                    Title = "New trip",
                    Content = nameBox,
                    PrimaryButtonText = "Create",
                    CloseButtonText = "Cancel",
                    XamlRoot = xamlRoot
                }.ShowAsync();
                if (create != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(nameBox.Text))
                    return false;
                targetTrip = await store.CreateTripAsync(nameBox.Text);
            }
            else
            {
                targetTrip = trips[picker.SelectedIndex];
            }
        }

        if (targetTrip is null)
            return false;

        var center = hypothesis.Center.Value;
        var waypoint = new Waypoint
        {
            TripId = targetTrip.Id,
            Name = hypothesis.Label,
            SiteType = siteType,
            Latitude = center.LatitudeDegrees,
            Longitude = center.LongitudeDegrees,
            Notes = HypothesisPresentation.BuildReasoningSummary(hypothesis, 3),
            SourceAnalysisSessionId = sessionId,
            SourceHypothesisId = hypothesis.Id,
            SourceVideoTitle = videoTitle
        };

        await store.AddWaypointAsync(waypoint);
        await ShowMessageAsync(xamlRoot, "Added to trip", $"“{hypothesis.Label}” is on trip “{targetTrip.Name}”. Open Trips to review.");
        return true;
    }

    private static Task ShowMessageAsync(XamlRoot xamlRoot, string title, string message) =>
        new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = xamlRoot
        }.ShowAsync().AsTask();
}
