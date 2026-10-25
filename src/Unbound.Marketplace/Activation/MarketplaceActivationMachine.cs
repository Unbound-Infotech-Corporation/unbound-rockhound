using Unbound.Marketplace.Checkout;

namespace Unbound.Marketplace.Activation;

public enum MarketplaceActivationPhase
{
    Idle,
    LaunchingCheckout,
    AwaitingPayment,
    PollingSession,
    Activating,
    Activated,
    NeedsCredential,
    Delegated,
    Failed
}

public enum MarketplaceActivationEffect
{
    None,
    LaunchCheckout,
    LookupSession,
    ActivateKey
}

public sealed record MarketplaceActivationSnapshot
{
    public MarketplaceActivationPhase Phase { get; init; } = MarketplaceActivationPhase.Idle;
    public MarketplaceActivationEffect Effect { get; init; } = MarketplaceActivationEffect.None;
    public string? Message { get; init; }
    public string? SessionId { get; init; }
    public string? LicenseKey { get; init; }
    public string? CatalogProductId { get; init; }
    public string? LicenseProductId { get; init; }
    public bool ActivatesLocally { get; init; }
    public int PollAttempt { get; init; }
    public int MaxPollAttempts { get; init; } = 8;
    public bool CanPaste { get; init; } = true;
    public bool CanPurchase { get; init; } = true;

    public static MarketplaceActivationSnapshot Idle(int maxPollAttempts = 8) => new()
    {
        Phase = MarketplaceActivationPhase.Idle,
        Effect = MarketplaceActivationEffect.None,
        MaxPollAttempts = Math.Max(1, maxPollAttempts),
        CanPaste = true,
        CanPurchase = true
    };

    public bool IsPurchaseInFlight => Phase is MarketplaceActivationPhase.LaunchingCheckout
        or MarketplaceActivationPhase.PollingSession
        or MarketplaceActivationPhase.Activating;
}

public static class MarketplaceActivationMachine
{
    public static bool ProductMatchesHost(string? productId, string? hostLicenseProductId)
    {
        if (string.IsNullOrWhiteSpace(hostLicenseProductId) || string.IsNullOrWhiteSpace(productId))
            return true;
        return string.Equals(productId.Trim(), hostLicenseProductId.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public static MarketplaceActivationSnapshot BeginCheckout(
        MarketplaceActivationSnapshot current,
        string catalogProductId,
        string? licenseProductId,
        bool activatesLocally)
    {
        return new MarketplaceActivationSnapshot
        {
            Phase = MarketplaceActivationPhase.LaunchingCheckout,
            Effect = MarketplaceActivationEffect.LaunchCheckout,
            CatalogProductId = catalogProductId,
            LicenseProductId = licenseProductId,
            ActivatesLocally = activatesLocally,
            MaxPollAttempts = Math.Max(1, current.MaxPollAttempts),
            Message = "Opening Stripe Checkout in your browser…",
            CanPaste = false,
            CanPurchase = false
        };
    }

    public static MarketplaceActivationSnapshot CheckoutOpened(
        MarketplaceActivationSnapshot current,
        string? sessionId,
        bool activatesLocally)
    {
        if (!activatesLocally)
        {
            return current with
            {
                Phase = MarketplaceActivationPhase.AwaitingPayment,
                Effect = MarketplaceActivationEffect.None,
                SessionId = sessionId,
                ActivatesLocally = false,
                Message = "Stripe Checkout is open in your browser. Finish payment there, then activate that license in its own Unbound Infotech app.",
                CanPaste = false,
                CanPurchase = true
            };
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return current with
            {
                Phase = MarketplaceActivationPhase.NeedsCredential,
                Effect = MarketplaceActivationEffect.None,
                ActivatesLocally = true,
                Message = "Stripe Checkout is open. After you pay, paste the license key or the checkout session id (cs_…).",
                CanPaste = true,
                CanPurchase = true
            };
        }

        var masked = CheckoutSessionIds.Mask(sessionId);
        return current with
        {
            Phase = MarketplaceActivationPhase.PollingSession,
            Effect = MarketplaceActivationEffect.LookupSession,
            SessionId = sessionId,
            ActivatesLocally = true,
            PollAttempt = 0,
            Message = $"Checkout is open in your browser ({masked}). This page will pick up the license after payment.",
            CanPaste = true,
            CanPurchase = false
        };
    }

    public static MarketplaceActivationSnapshot CheckoutFailed(MarketplaceActivationSnapshot current, string error)
    {
        return current with
        {
            Phase = MarketplaceActivationPhase.Failed,
            Effect = MarketplaceActivationEffect.None,
            Message = string.IsNullOrWhiteSpace(error) ? "Stripe Checkout did not start." : error.Trim(),
            CanPaste = true,
            CanPurchase = true
        };
    }

    public static MarketplaceActivationSnapshot OnSessionLookup(
        MarketplaceActivationSnapshot current,
        MarketplaceSessionLookup lookup,
        string? hostLicenseProductId)
    {
        switch (lookup.Kind)
        {
            case MarketplaceSessionLookupKind.Pending:
            {
                var attempt = current.PollAttempt + 1;
                if (attempt >= Math.Max(1, current.MaxPollAttempts))
                {
                    return current with
                    {
                        Phase = MarketplaceActivationPhase.NeedsCredential,
                        Effect = MarketplaceActivationEffect.None,
                        PollAttempt = attempt,
                        Message = "The license is not ready yet. Paste the key from your email, or choose Check again in a moment.",
                        CanPaste = true,
                        CanPurchase = true
                    };
                }

                return current with
                {
                    Phase = MarketplaceActivationPhase.PollingSession,
                    Effect = MarketplaceActivationEffect.LookupSession,
                    PollAttempt = attempt,
                    Message = lookup.Message ?? "Payment not confirmed yet. Leave this page open — Stripe usually finishes in a few seconds.",
                    CanPaste = true,
                    CanPurchase = false
                };
            }
            case MarketplaceSessionLookupKind.Found:
            {
                if (string.IsNullOrWhiteSpace(lookup.LicenseKey))
                {
                    return current with
                    {
                        Phase = MarketplaceActivationPhase.NeedsCredential,
                        Effect = MarketplaceActivationEffect.None,
                        Message = "The checkout session was found, but it did not include a license key. Paste the key from your email.",
                        CanPaste = true,
                        CanPurchase = true
                    };
                }

                if (!ProductMatchesHost(lookup.ProductId, hostLicenseProductId))
                {
                    return current with
                    {
                        Phase = MarketplaceActivationPhase.Delegated,
                        Effect = MarketplaceActivationEffect.None,
                        LicenseKey = null,
                        Message = "This purchase belongs to another Unbound Infotech app. Activate it there. This install only activates its own license.",
                        CanPaste = true,
                        CanPurchase = true
                    };
                }

                return current with
                {
                    Phase = MarketplaceActivationPhase.Activating,
                    Effect = MarketplaceActivationEffect.ActivateKey,
                    LicenseKey = lookup.LicenseKey.Trim(),
                    Message = "License found. Activating this PC…",
                    CanPaste = false,
                    CanPurchase = false
                };
            }
            case MarketplaceSessionLookupKind.NotConfigured:
                return current with
                {
                    Phase = MarketplaceActivationPhase.NeedsCredential,
                    Effect = MarketplaceActivationEffect.None,
                    Message = lookup.Message ?? "Configure the cloud account before this PC can pick up a license automatically. You can still paste a key.",
                    CanPaste = true,
                    CanPurchase = true
                };
            default:
                return current with
                {
                    Phase = MarketplaceActivationPhase.NeedsCredential,
                    Effect = MarketplaceActivationEffect.None,
                    Message = lookup.Message ?? "The license server could not be reached. Paste the key from your email when you have it.",
                    CanPaste = true,
                    CanPurchase = true
                };
        }
    }

    public static MarketplaceActivationSnapshot OnKeyActivation(
        MarketplaceActivationSnapshot current,
        bool ok,
        string? error)
    {
        if (ok)
        {
            return current with
            {
                Phase = MarketplaceActivationPhase.Activated,
                Effect = MarketplaceActivationEffect.None,
                Message = "This PC is licensed. Owned products are updated.",
                CanPaste = true,
                CanPurchase = true
            };
        }

        return current with
        {
            Phase = MarketplaceActivationPhase.Failed,
            Effect = MarketplaceActivationEffect.None,
            Message = string.IsNullOrWhiteSpace(error) ? "Activation failed." : error.Trim(),
            CanPaste = true,
            CanPurchase = true
        };
    }

    public static MarketplaceActivationSnapshot SubmitCredential(MarketplaceActivationSnapshot current, string? raw)
    {
        var text = raw?.Trim() ?? "";
        var sessionId = CheckoutSessionIds.TryExtract(text);
        if (sessionId is not null)
        {
            return current with
            {
                Phase = MarketplaceActivationPhase.PollingSession,
                Effect = MarketplaceActivationEffect.LookupSession,
                SessionId = sessionId,
                LicenseKey = null,
                PollAttempt = 0,
                ActivatesLocally = true,
                Message = $"Checking checkout session {CheckoutSessionIds.Mask(sessionId)}…",
                CanPaste = true,
                CanPurchase = false
            };
        }

        if (text.Length >= 8)
        {
            return current with
            {
                Phase = MarketplaceActivationPhase.Activating,
                Effect = MarketplaceActivationEffect.ActivateKey,
                LicenseKey = text,
                PollAttempt = current.PollAttempt,
                Message = "Activating this PC…",
                CanPaste = false,
                CanPurchase = false
            };
        }

        return current with
        {
            Effect = MarketplaceActivationEffect.None,
            Message = "Paste a license key or a Stripe checkout session id that starts with cs_."
        };
    }

    public static MarketplaceActivationSnapshot CheckAgain(MarketplaceActivationSnapshot current)
    {
        if (string.IsNullOrWhiteSpace(current.SessionId))
        {
            return current with
            {
                Phase = MarketplaceActivationPhase.NeedsCredential,
                Effect = MarketplaceActivationEffect.None,
                Message = "Paste the license key, or the checkout session id from the Stripe receipt.",
                CanPaste = true,
                CanPurchase = true
            };
        }

        return current with
        {
            Phase = MarketplaceActivationPhase.PollingSession,
            Effect = MarketplaceActivationEffect.LookupSession,
            PollAttempt = 0,
            Message = $"Checking checkout session {CheckoutSessionIds.Mask(current.SessionId)} again…",
            CanPaste = true,
            CanPurchase = false
        };
    }

    public static MarketplaceActivationSnapshot Reset(MarketplaceActivationSnapshot current) =>
        MarketplaceActivationSnapshot.Idle(current.MaxPollAttempts);
}
