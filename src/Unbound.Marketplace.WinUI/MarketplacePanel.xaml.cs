using Unbound.Marketplace;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Unbound.Marketplace.WinUI;

public sealed partial class MarketplacePanel : UserControl
{
    private bool _started;

    public MarketplacePanel()
    {
        ViewModel = new MarketplaceViewModel();
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public MarketplaceViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_started)
            return;
        _started = true;

        var host = MarketplaceHostContext.Current;
        if (host is null)
        {
            ViewModel.MarkUnconfigured();
            return;
        }

        ViewModel.Attach(host);
        host.Ownership.OwnershipChanged += OnOwnershipChanged;
        await ViewModel.LoadCatalogAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Cancel();
        if (MarketplaceHostContext.Current is { } host)
            host.Ownership.OwnershipChanged -= OnOwnershipChanged;
        _started = false;
    }

    private void OnOwnershipChanged(object? sender, EventArgs e)
    {
        if (DispatcherQueue.HasThreadAccess)
            ViewModel.ApplyOwnership();
        else
            DispatcherQueue.TryEnqueue(ViewModel.ApplyOwnership);
    }
}
