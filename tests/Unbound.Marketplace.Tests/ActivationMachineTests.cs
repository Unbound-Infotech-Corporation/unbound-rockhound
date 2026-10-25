using FluentAssertions;
using Unbound.Marketplace.Activation;
using Unbound.Marketplace.Catalog;
using Unbound.Marketplace.Checkout;

namespace Unbound.Marketplace.Tests;

public class ActivationMachineTests
{
    [Fact]
    public void Checkout_ForThisApp_PollsTheSessionEmbeddedInTheStripeUrl()
    {
        var idle = MarketplaceActivationSnapshot.Idle(maxPollAttempts: 3);
        var launching = MarketplaceActivationMachine.BeginCheckout(idle, "6a9949122f0c8cf48076190a", "unbound-rockhound", activatesLocally: true);
        launching.Phase.Should().Be(MarketplaceActivationPhase.LaunchingCheckout);
        launching.Effect.Should().Be(MarketplaceActivationEffect.LaunchCheckout);
        launching.CanPurchase.Should().BeFalse();

        var session = CheckoutSessionIds.TryExtract(
            "https://checkout.stripe.com/c/pay/cs_test_abc123#fid");
        session.Should().Be("cs_test_abc123");

        var polling = MarketplaceActivationMachine.CheckoutOpened(launching, session, activatesLocally: true);
        polling.Phase.Should().Be(MarketplaceActivationPhase.PollingSession);
        polling.Effect.Should().Be(MarketplaceActivationEffect.LookupSession);
        polling.SessionId.Should().Be("cs_test_abc123");
        polling.CanPaste.Should().BeTrue();
        polling.Message.Should().Contain("cs_test_abc123");
    }

    [Fact]
    public void Checkout_WithoutASession_WaitsForAPastedKey()
    {
        var opened = MarketplaceActivationMachine.CheckoutOpened(
            MarketplaceActivationMachine.BeginCheckout(MarketplaceActivationSnapshot.Idle(), "id", "unbound-rockhound", true),
            sessionId: null,
            activatesLocally: true);

        opened.Phase.Should().Be(MarketplaceActivationPhase.NeedsCredential);
        opened.Effect.Should().Be(MarketplaceActivationEffect.None);
        opened.CanPaste.Should().BeTrue();
    }

    [Fact]
    public void Checkout_ForAnotherApp_DoesNotPollThisInstall()
    {
        var opened = MarketplaceActivationMachine.CheckoutOpened(
            MarketplaceActivationMachine.BeginCheckout(MarketplaceActivationSnapshot.Idle(), "heirloom", null, activatesLocally: false),
            "cs_test_otherapp",
            activatesLocally: false);

        opened.Phase.Should().Be(MarketplaceActivationPhase.AwaitingPayment);
        opened.Effect.Should().Be(MarketplaceActivationEffect.None);
        opened.CanPaste.Should().BeFalse();
    }

    [Fact]
    public void PendingLookups_StopAtTheAttemptCap_AndKeepPasteAvailable()
    {
        var snapshot = MarketplaceActivationMachine.CheckoutOpened(
            MarketplaceActivationMachine.BeginCheckout(MarketplaceActivationSnapshot.Idle(2), "id", "unbound-rockhound", true),
            "cs_test_wait",
            true);
        var pending = new MarketplaceSessionLookup(MarketplaceSessionLookupKind.Pending, null, null, "not ready");

        snapshot = MarketplaceActivationMachine.OnSessionLookup(snapshot, pending, "unbound-rockhound");
        snapshot.Phase.Should().Be(MarketplaceActivationPhase.PollingSession);
        snapshot.Effect.Should().Be(MarketplaceActivationEffect.LookupSession);
        snapshot.PollAttempt.Should().Be(1);

        snapshot = MarketplaceActivationMachine.OnSessionLookup(snapshot, pending, "unbound-rockhound");
        snapshot.Phase.Should().Be(MarketplaceActivationPhase.NeedsCredential);
        snapshot.Effect.Should().Be(MarketplaceActivationEffect.None);
        snapshot.PollAttempt.Should().Be(2);
        snapshot.CanPaste.Should().BeTrue();
        snapshot.CanPurchase.Should().BeTrue();
    }

    [Fact]
    public void FoundHostLicense_RequestsOneActivation()
    {
        var polling = PollingSnapshot();
        var next = MarketplaceActivationMachine.OnSessionLookup(
            polling,
            new MarketplaceSessionLookup(MarketplaceSessionLookupKind.Found, "UR-ABCD-EFGH-JKLM-NPQR", "unbound-rockhound", null),
            "unbound-rockhound");

        next.Phase.Should().Be(MarketplaceActivationPhase.Activating);
        next.Effect.Should().Be(MarketplaceActivationEffect.ActivateKey);
        next.LicenseKey.Should().Be("UR-ABCD-EFGH-JKLM-NPQR");
    }

    [Fact]
    public void FoundForeignLicense_DoesNotActivateHere()
    {
        var next = MarketplaceActivationMachine.OnSessionLookup(
            PollingSnapshot(),
            new MarketplaceSessionLookup(MarketplaceSessionLookupKind.Found, "HU-AAAA-BBBB-CCCC-DDDD", "heirloom-unbound", null),
            "unbound-rockhound");

        next.Phase.Should().Be(MarketplaceActivationPhase.Delegated);
        next.Effect.Should().Be(MarketplaceActivationEffect.None);
        next.LicenseKey.Should().BeNull();
    }

    [Fact]
    public void ActivationSuccess_BecomesActivated_FailureStaysPasteable()
    {
        var activating = MarketplaceActivationMachine.OnSessionLookup(
            PollingSnapshot(),
            new MarketplaceSessionLookup(MarketplaceSessionLookupKind.Found, "UR-ABCD-EFGH-JKLM-NPQR", "unbound-rockhound", null),
            "unbound-rockhound");

        var ok = MarketplaceActivationMachine.OnKeyActivation(activating, ok: true, error: null);
        ok.Phase.Should().Be(MarketplaceActivationPhase.Activated);
        ok.Effect.Should().Be(MarketplaceActivationEffect.None);

        var failed = MarketplaceActivationMachine.OnKeyActivation(activating, ok: false, error: "License key not found.");
        failed.Phase.Should().Be(MarketplaceActivationPhase.Failed);
        failed.CanPaste.Should().BeTrue();
        failed.Message.Should().Be("License key not found.");
    }

    [Fact]
    public void Paste_RoutesSessionIdsAndKeys_AndRejectsFragments()
    {
        var idle = MarketplaceActivationSnapshot.Idle();

        var session = MarketplaceActivationMachine.SubmitCredential(idle, " receipt cs_live_ABC123xyz ");
        session.Phase.Should().Be(MarketplaceActivationPhase.PollingSession);
        session.Effect.Should().Be(MarketplaceActivationEffect.LookupSession);
        session.SessionId.Should().Be("cs_live_ABC123xyz");

        var key = MarketplaceActivationMachine.SubmitCredential(idle, "UR-ABCD-EFGH-JKLM-NPQR");
        key.Phase.Should().Be(MarketplaceActivationPhase.Activating);
        key.Effect.Should().Be(MarketplaceActivationEffect.ActivateKey);
        key.LicenseKey.Should().Be("UR-ABCD-EFGH-JKLM-NPQR");

        var junk = MarketplaceActivationMachine.SubmitCredential(idle, "nope");
        junk.Phase.Should().Be(MarketplaceActivationPhase.Idle);
        junk.Effect.Should().Be(MarketplaceActivationEffect.None);
        junk.Message.Should().Contain("cs_");
    }

    [Fact]
    public void CheckAgain_ResetsThePollCounter_WhenASessionIsKnown()
    {
        var exhausted = MarketplaceActivationMachine.OnSessionLookup(
            MarketplaceActivationMachine.OnSessionLookup(PollingSnapshot(maxAttempts: 1), Pending(), "unbound-rockhound"),
            Pending(),
            "unbound-rockhound");
        exhausted.Phase.Should().Be(MarketplaceActivationPhase.NeedsCredential);

        var again = MarketplaceActivationMachine.CheckAgain(exhausted with { SessionId = "cs_test_again" });
        again.Phase.Should().Be(MarketplaceActivationPhase.PollingSession);
        again.PollAttempt.Should().Be(0);
        again.Effect.Should().Be(MarketplaceActivationEffect.LookupSession);
    }

    [Fact]
    public void CheckoutFailure_DoesNotClearPaste()
    {
        var failed = MarketplaceActivationMachine.CheckoutFailed(
            MarketplaceActivationMachine.BeginCheckout(MarketplaceActivationSnapshot.Idle(), "id", "unbound-rockhound", true),
            "Could not reach Unbound Infotech checkout.");
        failed.Phase.Should().Be(MarketplaceActivationPhase.Failed);
        failed.Effect.Should().Be(MarketplaceActivationEffect.None);
        failed.CanPaste.Should().BeTrue();
        failed.CanPurchase.Should().BeTrue();
    }

    [Fact]
    public void NotConfigured_AsksForAPastedKey()
    {
        var next = MarketplaceActivationMachine.OnSessionLookup(
            PollingSnapshot(),
            new MarketplaceSessionLookup(MarketplaceSessionLookupKind.NotConfigured, null, null, "Configure Supabase first."),
            "unbound-rockhound");
        next.Phase.Should().Be(MarketplaceActivationPhase.NeedsCredential);
        next.Effect.Should().Be(MarketplaceActivationEffect.None);
        next.Message.Should().Be("Configure Supabase first.");
    }

    [Theory]
    [InlineData("""{"url":"https://checkout.stripe.com/c/pay/cs_test_frombody"}""", true, "cs_test_frombody")]
    [InlineData("""{"data":{"url":"https://checkout.stripe.com/c/pay/cs_live_nested99"}}""", true, "cs_live_nested99")]
    [InlineData("""{"error":"Entity Product with ID missing not found"}""", false, null)]
    public void CheckoutResponse_ReadsStripeUrlOrError(string json, bool ok, string? session)
    {
        var parsed = CheckoutResponseParser.TryReadUrl(json, out var url, out var error);
        parsed.Should().Be(ok);
        if (ok)
        {
            CheckoutSessionIds.TryExtract(url).Should().Be(session);
            CheckoutResponseParser.IsStripeCheckoutUrl(new Uri(url!)).Should().BeTrue();
        }
        else
        {
            error.Should().Contain("not found");
        }
    }

    [Fact]
    public void SessionLookupParser_MapsPendingFoundAndFailure()
    {
        MarketplaceSessionLookupParser.Parse(System.Net.HttpStatusCode.NotFound, """{"ok":false,"pending":true,"message":"License not ready yet"}""")
            .Kind.Should().Be(MarketplaceSessionLookupKind.Pending);

        var found = MarketplaceSessionLookupParser.Parse(System.Net.HttpStatusCode.OK, """
            {"ok":true,"licenseKey":"UR-ABCD-EFGH-JKLM-NPQR","productId":"unbound-rockhound"}
            """);
        found.Kind.Should().Be(MarketplaceSessionLookupKind.Found);
        found.LicenseKey.Should().Be("UR-ABCD-EFGH-JKLM-NPQR");
        found.ProductId.Should().Be("unbound-rockhound");

        MarketplaceSessionLookupParser.Parse(System.Net.HttpStatusCode.BadRequest, """{"error":"Invalid checkout session id"}""")
            .Kind.Should().Be(MarketplaceSessionLookupKind.Failed);
    }

    [Fact]
    public async Task PurchaseFlow_PollsThenActivatesOnce()
    {
        var calls = 0;
        var sessions = new FakeSessions(_ =>
        {
            calls++;
            return calls == 1
                ? new MarketplaceSessionLookup(MarketplaceSessionLookupKind.Pending, null, null, "not ready")
                : new MarketplaceSessionLookup(MarketplaceSessionLookupKind.Found, "UR-AAAA-BBBB-CCCC-DDDD", "unbound-rockhound", null);
        });
        var activator = new FakeActivator(true, null);
        var flow = Flow(sessions, activator, new FakeCheckout(new CheckoutLaunchResult(true, "https://checkout.stripe.com/c/pay/cs_test_flow1", "cs_test_flow1", null)));

        var snapshot = await flow.BuyAsync(HostProduct(), progress: null);

        snapshot.Phase.Should().Be(MarketplaceActivationPhase.Activated);
        activator.Keys.Should().Equal("UR-AAAA-BBBB-CCCC-DDDD");
        calls.Should().Be(2);
    }

    [Fact]
    public async Task PurchaseFlow_DoesNotActivateAnotherProduct()
    {
        var sessions = new FakeSessions(_ => throw new InvalidOperationException("should not poll"));
        var activator = new FakeActivator(true, null);
        var flow = Flow(sessions, activator, new FakeCheckout(new CheckoutLaunchResult(true, "https://checkout.stripe.com/c/pay/cs_test_other", "cs_test_other", null)));

        var snapshot = await flow.BuyAsync(HostProduct() with { LicenseProductId = "heirloom-unbound", Name = "Heirloom Unbound" }, null);

        snapshot.Phase.Should().Be(MarketplaceActivationPhase.AwaitingPayment);
        snapshot.Effect.Should().Be(MarketplaceActivationEffect.None);
        activator.Keys.Should().BeEmpty();
    }

    [Fact]
    public async Task PurchaseFlow_PasteKeyUsesTheHostActivatorOnly()
    {
        var activator = new FakeActivator(false, "This key is not for Unbound Rockhound.");
        var flow = Flow(new FakeSessions(_ => throw new InvalidOperationException("no session")), activator, new FakeCheckout(new CheckoutLaunchResult(false, null, null, "unused")));

        var snapshot = await flow.SubmitCredentialAsync(MarketplaceActivationSnapshot.Idle(), "UR-ZZZZ-YYYY-XXXX-WWWW", null);

        snapshot.Phase.Should().Be(MarketplaceActivationPhase.Failed);
        snapshot.CanPaste.Should().BeTrue();
        activator.Keys.Should().Equal("UR-ZZZZ-YYYY-XXXX-WWWW");
    }

    private static MarketplaceActivationSnapshot PollingSnapshot(int maxAttempts = 8) =>
        MarketplaceActivationMachine.CheckoutOpened(
            MarketplaceActivationMachine.BeginCheckout(MarketplaceActivationSnapshot.Idle(maxAttempts), "6a9949122f0c8cf48076190a", "unbound-rockhound", true),
            "cs_test_poll",
            true);

    private static MarketplaceSessionLookup Pending() =>
        new(MarketplaceSessionLookupKind.Pending, null, null, "not ready");

    private static MarketplaceProduct HostProduct() => new()
    {
        Id = "6a9949122f0c8cf48076190a",
        Name = "Unbound Rockhound",
        Status = "active",
        LicenseProductId = "unbound-rockhound",
        Price = 39.99m,
        PriceLabel = "$39.99"
    };

    private static MarketplacePurchaseFlow Flow(IMarketplaceSessionClient sessions, IMarketplaceKeyActivator activator, ICheckoutLauncher checkout) =>
        new(
            new MarketplaceOptions
            {
                HostLicenseProductId = "unbound-rockhound",
                HostProductDisplayName = "Unbound Rockhound",
                MaxPollAttempts = 4,
                PollInterval = TimeSpan.Zero
            },
            checkout,
            sessions,
            activator,
            (_, _) => Task.CompletedTask);

    private sealed class FakeCheckout : ICheckoutLauncher
    {
        private readonly CheckoutLaunchResult _result;
        public FakeCheckout(CheckoutLaunchResult result) => _result = result;
        public Task<CheckoutLaunchResult> LaunchAsync(MarketplaceProduct product, CancellationToken cancellationToken = default) =>
            Task.FromResult(_result);
    }

    private sealed class FakeSessions : IMarketplaceSessionClient
    {
        private readonly Func<string, MarketplaceSessionLookup> _lookup;
        public FakeSessions(Func<string, MarketplaceSessionLookup> lookup) => _lookup = lookup;
        public Task<MarketplaceSessionLookup> LookupAsync(string checkoutSessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_lookup(checkoutSessionId));
    }

    private sealed class FakeActivator : IMarketplaceKeyActivator
    {
        private readonly bool _ok;
        private readonly string? _error;
        public List<string> Keys { get; } = new();
        public FakeActivator(bool ok, string? error)
        {
            _ok = ok;
            _error = error;
        }

        public Task<MarketplaceKeyActivation> ActivateAsync(string licenseKey, CancellationToken cancellationToken = default)
        {
            Keys.Add(licenseKey);
            return Task.FromResult(new MarketplaceKeyActivation(_ok, _error));
        }
    }
}
