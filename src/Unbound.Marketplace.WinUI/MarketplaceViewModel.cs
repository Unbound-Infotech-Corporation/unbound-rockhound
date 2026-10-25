using System.Collections.ObjectModel;
using Unbound.Marketplace;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Unbound.Marketplace.Activation;
using Unbound.Marketplace.Catalog;

namespace Unbound.Marketplace.WinUI;

public static class BoolVis
{
    public static Visibility Of(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}

public sealed class MarketplaceProductCard : ObservableObject
{
    private readonly Func<MarketplaceProductCard, Task> _buy;
    private MarketplaceOfferState _offer = MarketplaceOfferState.Buy;
    private bool _purchaseInFlight;
    private string _actionLabel = "Buy";
    private string _footnote;
    private Visibility _buyVisibility = Visibility.Visible;
    private Visibility _passiveVisibility = Visibility.Collapsed;

    public MarketplaceProductCard(MarketplaceProduct product, bool isThisApp, Func<MarketplaceProductCard, Task> buy)
    {
        Product = product;
        IsThisApp = isThisApp;
        _buy = buy;
        _footnote = MarketplaceOffer.Footnote(MarketplaceOfferState.Buy, isThisApp);
        BuyCommand = new AsyncRelayCommand(
            () => _buy(this),
            () => (_offer is MarketplaceOfferState.Buy or MarketplaceOfferState.GetIt) && !_purchaseInFlight);
        if (Uri.TryCreate(product.IconUrl, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps)
        {
            Icon = new BitmapImage(uri);
        }
    }

    public MarketplaceProduct Product { get; }
    public string Name => Product.Name;
    public string PriceLabel => string.IsNullOrWhiteSpace(Product.PriceLabel) ? "—" : Product.PriceLabel;
    public string Summary => string.IsNullOrWhiteSpace(Product.ShortDescription)
        ? "Software from Unbound Infotech."
        : Product.ShortDescription;
    public string Monogram => Product.Monogram;
    public bool IsThisApp { get; }
    public Visibility ThisAppVisibility => IsThisApp ? Visibility.Visible : Visibility.Collapsed;
    public ImageSource? Icon { get; }
    public Visibility IconVisibility => Icon is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility MonogramVisibility => Icon is null ? Visibility.Visible : Visibility.Collapsed;
    public string Footnote
    {
        get => _footnote;
        private set => SetProperty(ref _footnote, value);
    }

    public MarketplaceOfferState Offer => _offer;
    public IAsyncRelayCommand BuyCommand { get; }

    public string ActionLabel
    {
        get => _actionLabel;
        private set => SetProperty(ref _actionLabel, value);
    }

    public Visibility BuyVisibility
    {
        get => _buyVisibility;
        private set => SetProperty(ref _buyVisibility, value);
    }

    public Visibility PassiveVisibility
    {
        get => _passiveVisibility;
        private set => SetProperty(ref _passiveVisibility, value);
    }

    public void Apply(MarketplaceOfferState offer, bool purchaseInFlight, bool thisCardBusy)
    {
        _offer = offer;
        _purchaseInFlight = purchaseInFlight;
        ActionLabel = thisCardBusy ? "Opening…" : MarketplaceOffer.ActionLabel(offer);
        Footnote = MarketplaceOffer.Footnote(offer, IsThisApp);
        var showAction = offer is MarketplaceOfferState.Buy or MarketplaceOfferState.GetIt;
        BuyVisibility = showAction ? Visibility.Visible : Visibility.Collapsed;
        PassiveVisibility = showAction ? Visibility.Collapsed : Visibility.Visible;
        BuyCommand.NotifyCanExecuteChanged();
    }
}

public sealed class MarketplaceViewModel : ObservableObject
{
    private MarketplaceHost? _host;
    private MarketplaceActivationSnapshot _snapshot = MarketplaceActivationSnapshot.Idle();
    private CancellationTokenSource? _work;
    private string? _busyProductId;
    private string? _catalogNotice;

    private bool _isLoading;
    private bool _isOffline;
    private bool _isFaulted;
    private bool _statusIsOpen;
    private bool _showCredential = true;
    private bool _checkAgainVisible;
    private string _statusTitle = "Unbound Marketplace";
    private string _statusMessage = "";
    private string _credentialText = "";
    private InfoBarSeverity _statusSeverity = InfoBarSeverity.Informational;

    public MarketplaceViewModel()
    {
        RefreshCommand = new AsyncRelayCommand(LoadCatalogAsync);
        ActivateCommand = new AsyncRelayCommand(SubmitAsync);
        CheckAgainCommand = new AsyncRelayCommand(CheckAgainAsync);
    }

    public ObservableCollection<MarketplaceProductCard> Products { get; } = new();

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand ActivateCommand { get; }
    public IAsyncRelayCommand CheckAgainCommand { get; }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool IsOffline
    {
        get => _isOffline;
        private set => SetProperty(ref _isOffline, value);
    }

    public bool IsFaulted
    {
        get => _isFaulted;
        private set
        {
            if (SetProperty(ref _isFaulted, value))
                OnPropertyChanged(nameof(ShowEmptyError));
        }
    }

    public bool StatusIsOpen
    {
        get => _statusIsOpen;
        private set => SetProperty(ref _statusIsOpen, value);
    }

    public string StatusTitle
    {
        get => _statusTitle;
        private set => SetProperty(ref _statusTitle, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public InfoBarSeverity StatusSeverity
    {
        get => _statusSeverity;
        private set => SetProperty(ref _statusSeverity, value);
    }

    public bool ShowCredential
    {
        get => _showCredential;
        private set => SetProperty(ref _showCredential, value);
    }

    public bool CheckAgainVisible
    {
        get => _checkAgainVisible;
        private set => SetProperty(ref _checkAgainVisible, value);
    }

    public string CredentialText
    {
        get => _credentialText;
        set => SetProperty(ref _credentialText, value);
    }

    public bool ShowLoadingOverlay => IsLoading && Products.Count == 0;
    public bool ShowEmptyError => IsFaulted && Products.Count == 0 && !IsLoading;
    public bool ShowCatalog => Products.Count > 0;

    public void Attach(MarketplaceHost host) => _host = host;

    public void MarkUnconfigured()
    {
        IsFaulted = true;
        IsLoading = false;
        ShowCredential = false;
        SetStatus(
            InfoBarSeverity.Error,
            "Unbound Marketplace",
            "This app has not connected the Unbound Marketplace host services.",
            open: true);
        RaiseChrome();
    }

    public void Cancel() => _work?.Cancel();

    public async Task LoadCatalogAsync()
    {
        if (_host is null)
        {
            MarkUnconfigured();
            return;
        }

        var token = RestartWork();
        IsLoading = true;
        RaiseChrome();
        if (Products.Count == 0)
        {
            SetStatus(InfoBarSeverity.Informational, "Loading", "Fetching the Unbound Marketplace catalog…", open: true);
        }

        try
        {
            var result = await _host.Catalog.LoadAsync(token).ConfigureAwait(true);
            token.ThrowIfCancellationRequested();
            Products.Clear();
            foreach (var product in result.Products)
            {
                if (!MarketplaceOffer.IsListed(product, _host.Options))
                    continue;

                var isThisApp = MarketplaceOffer.CanActivateHere(product, _host.Options);
                Products.Add(new MarketplaceProductCard(product, isThisApp, BuyAsync));
            }

            IsOffline = result.Source == MarketplaceCatalogSource.Fallback;
            IsFaulted = result.Source == MarketplaceCatalogSource.Unavailable;
            _catalogNotice = result.Notice;
            ApplyOwnership();
            PublishIdleStatus();
        }
        catch (OperationCanceledException)
        {
            // A newer refresh or purchase replaced this load.
        }
        catch (Exception)
        {
            IsFaulted = Products.Count == 0;
            IsOffline = false;
            SetStatus(
                InfoBarSeverity.Error,
                "Catalog unavailable",
                "The Unbound Marketplace catalog could not be loaded.",
                open: true);
        }
        finally
        {
            IsLoading = false;
            RaiseChrome();
        }
    }

    public void ApplyOwnership()
    {
        var owned = _host?.Ownership.GetOwnedLicenseProductIds();
        foreach (var card in Products)
        {
            var offer = MarketplaceOffer.Resolve(card.Product, owned, _host?.Options);
            card.Apply(
                offer,
                purchaseInFlight: _snapshot.IsPurchaseInFlight,
                thisCardBusy: _busyProductId == card.Product.Id && _snapshot.IsPurchaseInFlight);
        }
    }

    private async Task BuyAsync(MarketplaceProductCard card)
    {
        if (_host is null)
            return;

        if (card.Offer == MarketplaceOfferState.GetIt)
        {
            OpenGetIt(card);
            return;
        }

        var token = RestartWork();
        _busyProductId = card.Product.Id;
        var progress = new Progress<MarketplaceActivationSnapshot>(ApplySnapshot);
        try
        {
            _snapshot = await _host.CreateFlow().BuyAsync(card.Product, progress, token).ConfigureAwait(true);
            ApplySnapshot(_snapshot);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ApplySnapshot(MarketplaceActivationMachine.CheckoutFailed(_snapshot, ex.Message));
        }
        finally
        {
            if (!token.IsCancellationRequested)
                _busyProductId = null;
            ApplyOwnership();
        }
    }

    private async Task SubmitAsync()
    {
        if (_host is null)
            return;

        var token = RestartWork();
        _busyProductId = null;
        var progress = new Progress<MarketplaceActivationSnapshot>(ApplySnapshot);
        try
        {
            _snapshot = await _host.CreateFlow()
                .SubmitCredentialAsync(_snapshot, CredentialText, progress, token)
                .ConfigureAwait(true);
            ApplySnapshot(_snapshot);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ApplySnapshot(MarketplaceActivationMachine.CheckoutFailed(_snapshot, ex.Message));
        }
    }

    private async Task CheckAgainAsync()
    {
        if (_host is null)
            return;

        var token = RestartWork();
        var progress = new Progress<MarketplaceActivationSnapshot>(ApplySnapshot);
        try
        {
            _snapshot = await _host.CreateFlow()
                .CheckAgainAsync(_snapshot, progress, token)
                .ConfigureAwait(true);
            ApplySnapshot(_snapshot);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ApplySnapshot(MarketplaceActivationMachine.OnSessionLookup(
                _snapshot,
                new MarketplaceSessionLookup(MarketplaceSessionLookupKind.Failed, null, null, ex.Message),
                _host.Options.HostLicenseProductId));
        }
    }

    private void ApplySnapshot(MarketplaceActivationSnapshot snapshot)
    {
        _snapshot = snapshot;
        CheckAgainVisible = snapshot.CanPaste && !string.IsNullOrWhiteSpace(snapshot.SessionId);
        ShowCredential = snapshot.CanPaste || snapshot.Phase == MarketplaceActivationPhase.Idle;
        ApplyOwnership();

        if (snapshot.Phase == MarketplaceActivationPhase.Idle)
        {
            PublishIdleStatus();
            return;
        }

        var (severity, title) = snapshot.Phase switch
        {
            MarketplaceActivationPhase.Activated => (InfoBarSeverity.Success, "Licensed"),
            MarketplaceActivationPhase.Failed => (InfoBarSeverity.Error, "Needs attention"),
            MarketplaceActivationPhase.Delegated => (InfoBarSeverity.Informational, "Other Unbound Infotech app"),
            MarketplaceActivationPhase.NeedsCredential => (InfoBarSeverity.Warning, "Paste a license"),
            MarketplaceActivationPhase.PollingSession => (InfoBarSeverity.Informational, "Waiting for Stripe"),
            MarketplaceActivationPhase.Activating => (InfoBarSeverity.Informational, "Activating"),
            MarketplaceActivationPhase.LaunchingCheckout => (InfoBarSeverity.Informational, "Opening checkout"),
            MarketplaceActivationPhase.AwaitingPayment => (InfoBarSeverity.Informational, "Browser checkout"),
            _ => (InfoBarSeverity.Informational, "Unbound Marketplace")
        };
        SetStatus(severity, title, snapshot.Message ?? "", open: true);
    }

    private void PublishIdleStatus()
    {
        CheckAgainVisible = _snapshot.CanPaste && !string.IsNullOrWhiteSpace(_snapshot.SessionId);
        if (IsFaulted)
        {
            SetStatus(InfoBarSeverity.Error, "Catalog unavailable", _catalogNotice ?? "The Unbound Marketplace catalog is unavailable.", true);
            return;
        }

        if (Products.Count == 0 && !IsFaulted && !IsOffline)
        {
            SetStatus(
                InfoBarSeverity.Informational,
                "Unbound Marketplace",
                "No products are listed for this app right now.",
                open: true);
            return;
        }

        if (IsOffline)
        {
            SetStatus(InfoBarSeverity.Warning, "Offline", _catalogNotice ?? MarketplaceCatalogClient.OfflineNotice, true);
            return;
        }

        SetStatus(
            InfoBarSeverity.Informational,
            "Unbound Marketplace",
            "Buy opens Stripe Checkout in your browser. Get it opens a store page or download for titles this install does not activate.",
            open: false);
    }

    private void OpenGetIt(MarketplaceProductCard card)
    {
        var link = MarketplaceOffer.ExternalLink(card.Product);
        if (link is null || _host?.Browser is null)
        {
            SetStatus(
                InfoBarSeverity.Warning,
                "Get it",
                $"{card.Name} has no store page or download link yet.",
                open: true);
            return;
        }

        try
        {
            _host.Browser.Open(link);
            SetStatus(
                InfoBarSeverity.Informational,
                "Get it",
                $"Opened {card.Name} in your browser. This install does not activate that license.",
                open: true);
        }
        catch (Exception ex)
        {
            SetStatus(InfoBarSeverity.Error, "Could not open the link", ex.Message, open: true);
        }
    }

    private void SetStatus(InfoBarSeverity severity, string title, string message, bool open)
    {
        StatusSeverity = severity;
        StatusTitle = title;
        StatusMessage = message;
        StatusIsOpen = open;
    }

    private void RaiseChrome()
    {
        OnPropertyChanged(nameof(ShowLoadingOverlay));
        OnPropertyChanged(nameof(ShowEmptyError));
        OnPropertyChanged(nameof(ShowCatalog));
    }

    private CancellationToken RestartWork()
    {
        _work?.Cancel();
        _work = new CancellationTokenSource();
        return _work.Token;
    }
}
