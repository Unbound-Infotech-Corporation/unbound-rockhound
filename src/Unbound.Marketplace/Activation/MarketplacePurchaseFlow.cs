namespace Unbound.Marketplace.Activation;

public sealed record MarketplaceKeyActivation(bool Ok, string? Error);

/// <summary>
/// Host hook that binds a license key on this device. Implementations must call the app's
/// existing activate path once — the marketplace does not call license-activate on its own.
/// </summary>
public interface IMarketplaceKeyActivator
{
    Task<MarketplaceKeyActivation> ActivateAsync(string licenseKey, CancellationToken cancellationToken = default);
}

public interface IMarketplaceOwnershipSource
{
    event EventHandler? OwnershipChanged;
    IReadOnlySet<string> GetOwnedLicenseProductIds();
}

/// <summary>
/// Runs checkout launch, session polling, and the host activation hook.
/// State changes go through <see cref="MarketplaceActivationMachine"/> so the transitions stay testable.
/// </summary>
public sealed class MarketplacePurchaseFlow
{
    private readonly MarketplaceOptions _options;
    private readonly ICheckoutLauncher _checkout;
    private readonly IMarketplaceSessionClient _sessions;
    private readonly IMarketplaceKeyActivator _activator;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public MarketplacePurchaseFlow(
        MarketplaceOptions options,
        ICheckoutLauncher checkout,
        IMarketplaceSessionClient sessions,
        IMarketplaceKeyActivator activator,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _options = options;
        _checkout = checkout;
        _sessions = sessions;
        _activator = activator;
        _delay = delay ?? ((span, token) => Task.Delay(span, token));
    }

    public async Task<MarketplaceActivationSnapshot> BuyAsync(
        MarketplaceProduct product,
        IProgress<MarketplaceActivationSnapshot>? progress,
        CancellationToken cancellationToken = default)
    {
        var activatesLocally = !string.IsNullOrWhiteSpace(product.LicenseProductId)
            && MarketplaceActivationMachine.ProductMatchesHost(product.LicenseProductId, _options.HostLicenseProductId)
            && !string.IsNullOrWhiteSpace(_options.HostLicenseProductId);

        var snapshot = MarketplaceActivationMachine.BeginCheckout(
            MarketplaceActivationSnapshot.Idle(_options.MaxPollAttempts),
            product.Id,
            product.LicenseProductId,
            activatesLocally);
        progress?.Report(snapshot);

        CheckoutLaunchResult launch;
        try
        {
            launch = await _checkout.LaunchAsync(product, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            if (cancellationToken.IsCancellationRequested)
                throw;
            snapshot = MarketplaceActivationMachine.CheckoutFailed(
                snapshot,
                "Could not reach Unbound Infotech checkout. Check your connection and try again.");
            progress?.Report(snapshot);
            return snapshot;
        }

        snapshot = launch.Ok
            ? MarketplaceActivationMachine.CheckoutOpened(snapshot, launch.SessionId, activatesLocally)
            : launch.SessionId is not null
                ? MarketplaceActivationMachine.CheckoutOpened(snapshot, launch.SessionId, activatesLocally) with
                {
                    Message = launch.Error ?? snapshot.Message
                }
                : MarketplaceActivationMachine.CheckoutFailed(snapshot, launch.Error ?? "Stripe Checkout did not start.");
        progress?.Report(snapshot);
        return await DrainAsync(snapshot, progress, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MarketplaceActivationSnapshot> SubmitCredentialAsync(
        MarketplaceActivationSnapshot current,
        string? raw,
        IProgress<MarketplaceActivationSnapshot>? progress,
        CancellationToken cancellationToken = default)
    {
        var snapshot = MarketplaceActivationMachine.SubmitCredential(current, raw);
        progress?.Report(snapshot);
        return await DrainAsync(snapshot, progress, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MarketplaceActivationSnapshot> CheckAgainAsync(
        MarketplaceActivationSnapshot current,
        IProgress<MarketplaceActivationSnapshot>? progress,
        CancellationToken cancellationToken = default)
    {
        var snapshot = MarketplaceActivationMachine.CheckAgain(current);
        progress?.Report(snapshot);
        return await DrainAsync(snapshot, progress, cancellationToken).ConfigureAwait(false);
    }

    private async Task<MarketplaceActivationSnapshot> DrainAsync(
        MarketplaceActivationSnapshot snapshot,
        IProgress<MarketplaceActivationSnapshot>? progress,
        CancellationToken cancellationToken)
    {
        var delayed = false;
        while (snapshot.Effect is MarketplaceActivationEffect.LookupSession or MarketplaceActivationEffect.ActivateKey)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (snapshot.Effect == MarketplaceActivationEffect.LookupSession)
            {
                if (delayed && _options.PollInterval > TimeSpan.Zero)
                    await _delay(_options.PollInterval, cancellationToken).ConfigureAwait(false);
                delayed = true;

                if (string.IsNullOrWhiteSpace(snapshot.SessionId))
                {
                    snapshot = MarketplaceActivationMachine.OnSessionLookup(
                        snapshot,
                        new MarketplaceSessionLookup(MarketplaceSessionLookupKind.Failed, null, null, "No checkout session id is available."),
                        _options.HostLicenseProductId);
                    progress?.Report(snapshot);
                    continue;
                }

                var lookup = await _sessions.LookupAsync(snapshot.SessionId, cancellationToken).ConfigureAwait(false);
                snapshot = MarketplaceActivationMachine.OnSessionLookup(snapshot, lookup, _options.HostLicenseProductId);
                progress?.Report(snapshot);
                continue;
            }

            var key = snapshot.LicenseKey ?? "";
            var activation = await _activator.ActivateAsync(key, cancellationToken).ConfigureAwait(false);
            snapshot = MarketplaceActivationMachine.OnKeyActivation(snapshot, activation.Ok, activation.Error);
            progress?.Report(snapshot);
        }

        return snapshot;
    }
}
