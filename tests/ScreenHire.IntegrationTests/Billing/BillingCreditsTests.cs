using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ScreenHire.Application.Billing;
using ScreenHire.Application.Verification;
using ScreenHire.Domain.Billing;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Verification;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Verification.Llm;
using ScreenHire.IntegrationTests.Messaging;
using ScreenHire.IntegrationTests.Notifications;
using Wolverine;

namespace ScreenHire.IntegrationTests.Billing;

/// <summary>
/// Prepaid AI credits (ADR-0032, docs/architecture/billing.md "AI credits") on the shared stack, which runs the billing sandbox: that a
/// credit is added only by a confirmed payment and once, that each built-in AI call is one debit, how a refused auto-reload ends, the
/// alerts and the expiry, who may do what, and what the Billing overview says about the month. The rules themselves (lots, auto-reload,
/// alerts) are <c>AiCreditsTests</c> in the unit tests; here is the wiring. A handler that needs a stand-in for Stripe or a clock runs in
/// process against the stack's database, and a message the real worker must handle is published to its queue.
/// </summary>
public sealed class BillingCreditsTests(AppHostFixture fixture)
{
    // Far in the future, so the real worker's daily expiry sweep (which reads the real clock) never reaches rows these tests seed.
    private static readonly DateTimeOffset Future = new(2099, 6, 15, 6, 7, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // What the starting credit is, from the setting's default ($5.00, docs/architecture/billing.md), and the shipped margin (20%).
    private static decimal StartingCredit => new BillingAiOptions().TrialAllowance;

    // ---- A credit is added by a confirmed payment, once ----

    [Fact]
    public async Task A_credit_is_added_only_when_the_payment_is_confirmed_and_never_by_starting_the_checkout()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        (await owner.CreditsAsync()).At("balance").GetDecimal().Should().Be(StartingCredit);

        var checkout = (await owner.PostAsync($"{BillingTestSupport.Credits}/checkout", new JsonObject { ["amount"] = 25 })).Expect(HttpStatusCode.OK);

        checkout.Text("url").Should().StartWith(SandboxBillingProvider.CreditCheckoutPath);
        await Wait.ForNothingAsync(TimeSpan.FromSeconds(2), "a grant for a purchase nobody confirmed", Ct);
        var pending = await owner.CreditsAsync();
        pending.At("balance").GetDecimal().Should().Be(StartingCredit, "a purchase that is Pending grants nothing");
        pending.At("hasSavedCard").GetBoolean().Should().BeFalse();
        (await owner.GetAsync($"{BillingTestSupport.Credits}/purchases")).Expect(HttpStatusCode.OK).Count("purchases").Should().Be(1, "only the starting credit: an unpaid Checkout is not a purchase");

        var (status, location) = await fixture.FollowAsync(checkout.Text("url"));

        status.Should().Be(HttpStatusCode.Redirect);
        location!.ToString().Should().Contain("credits=success");
        await WaitForBalanceAsync(owner, StartingCredit + 25m);
        var purchases = (await owner.GetAsync($"{BillingTestSupport.Credits}/purchases")).Expect(HttpStatusCode.OK);
        purchases.Count("purchases").Should().Be(2);
        purchases.At("purchases.0").GetProperty("source").GetString().Should().Be("Checkout");
        purchases.At("purchases.0").GetProperty("status").GetString().Should().Be("Paid");
        purchases.At("purchases.0").GetProperty("amount").GetDecimal().Should().Be(25m);
        purchases.At("purchases.0").GetProperty("expiresAt").ValueKind.Should().Be(JsonValueKind.String, "a credit lasts the configured months");
        await using var db = await VerificationSupport.TenantDbAsync(fixture, owner.OrganizationId);
        (await db.CreditSettings.AsNoTracking().SingleAsync(Ct)).PaymentMethodId.Should().Be(SandboxBillingProvider.SandboxCard, "the Checkout saved the card for auto-reload");
        (await owner.CreditsAsync()).At("hasSavedCard").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task A_confirmation_delivered_twice_for_one_purchase_adds_the_credit_once()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await EnsureStartingCreditAsync(owner.OrganizationId);
        var purchase = await SeedPurchaseAsync(owner.OrganizationId, CreditPurchaseSource.Checkout, 10m);
        var message = new GrantAiCredits(owner.OrganizationId, purchase.Id, "pi_fictional_one");

        var first = await GrantAsync(message);
        var second = await GrantAsync(message with { ProviderPaymentId = "pi_fictional_two" });

        first.OfType<ResumeOrganizationAiChecks>().Should().ContainSingle("the checks a lack of credits paused are asked for again");
        second.Should().BeEmpty("the second delivery finds the purchase paid");
        await using var db = await VerificationSupport.TenantDbAsync(fixture, owner.OrganizationId);
        var grants = await db.CreditEntries.AsNoTracking().Where(e => e.PurchaseId == purchase.Id).ToListAsync(Ct);
        grants.Should().ContainSingle().Which.Micros.Should().Be(10_000_000);
        (await AiCreditLedger.BalanceMicrosAsync(db, Ct)).Should().Be(15_000_000);
        (await db.CreditPurchases.AsNoTracking().SingleAsync(Ct)).Should().Match<CreditPurchase>(p => p.Status == CreditPurchaseStatus.Paid && p.ProviderPaymentId == "pi_fictional_one");
        message.IdempotencyKey.Should().Be((message with { ProviderPaymentId = "other" }).IdempotencyKey, "the worker's inbox keys the grant on the purchase");
    }

    [Fact]
    public async Task A_confirmation_for_a_purchase_that_does_not_exist_adds_nothing()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await EnsureStartingCreditAsync(owner.OrganizationId);

        var outgoing = await GrantAsync(new GrantAiCredits(owner.OrganizationId, Guid.CreateVersion7(), "pi_fictional"));

        outgoing.Should().BeEmpty();
        await using var db = await VerificationSupport.TenantDbAsync(fixture, owner.OrganizationId);
        (await AiCreditLedger.BalanceMicrosAsync(db, Ct)).Should().Be(5_000_000);
    }

    // ---- What an AI call draws ----

    [Fact]
    public async Task Each_AI_call_on_ScreenHires_key_is_one_debit_of_its_cost_plus_the_margin_and_an_own_keys_call_draws_nothing()
    {
        using var builtIn = await fixture.SignedInWithOrganizationAsync();
        using var ownKey = await fixture.SignedInWithOrganizationAsync();
        await ApplyWithCheckAsync(builtIn, null);
        await ApplyWithCheckAsync(ownKey, new JsonObject
        {
            ["llmConsistencyCheck"] = true,
            ["modelSource"] = "OwnKey",
            ["provider"] = "OpenRouter",
            ["model"] = "vendor/model",
            ["apiKey"] = "own-key-credits-test",
        });

        await using (var db = await VerificationSupport.TenantDbAsync(fixture, builtIn.OrganizationId))
        {
            var calls = await db.AiUsageCalls.AsNoTracking().ToListAsync(Ct);
            var debits = await db.CreditEntries.AsNoTracking().Where(e => e.Kind == CreditEntryKind.Usage).ToListAsync(Ct);
            calls.Should().NotBeEmpty().And.OnlyContain(c => c.KeySource == AiKeySource.BuiltIn && c.Cost > 0m);
            debits.Should().HaveCount(calls.Count, "one debit for each call");
            foreach (var call in calls)
            {
                // The brief's rule written out: the provider's price times 1.20, in whole micro-dollars.
                var expected = (long)Math.Round(call.Cost!.Value * 1.20m * 1_000_000m, MidpointRounding.AwayFromZero);
                debits.Should().ContainSingle(d => d.AiCallId == call.Id).Which.Micros.Should().Be(-expected);
            }

            (await AiCreditLedger.BalanceMicrosAsync(db, Ct)).Should().Be(5_000_000 - debits.Sum(d => -d.Micros));
        }

        var deductions = (await builtIn.GetAsync($"{BillingTestSupport.Credits}/deductions")).Expect(HttpStatusCode.OK);
        deductions.Count("deductions").Should().Be(1, "the calls of one check are one line");
        deductions.At("deductions.0").GetProperty("kind").GetString().Should().Be("Check");
        deductions.At("deductions.0").GetProperty("calls").GetInt32().Should().BeGreaterThan(1);
        deductions.At("deductions.0").GetProperty("candidateName").GetString().Should().Be(SyntheticApplicants.GenuineHuman.FullName);

        await using var own = await VerificationSupport.TenantDbAsync(fixture, ownKey.OrganizationId);
        (await own.AiUsageCalls.AsNoTracking().CountAsync(Ct)).Should().BeGreaterThan(0);
        (await own.AiUsageCalls.AsNoTracking().AllAsync(c => c.KeySource == AiKeySource.OwnKey, Ct)).Should().BeTrue();
        (await own.CreditEntries.AsNoTracking().CountAsync(e => e.Kind == CreditEntryKind.Usage, Ct)).Should().Be(0);
        var credits = await ownKey.CreditsAsync();
        credits.At("balance").GetDecimal().Should().Be(StartingCredit);
        credits.At("ownKey").GetBoolean().Should().BeTrue();
        credits.At("paused").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task An_empty_balance_says_the_check_is_paused_and_a_bought_credit_says_it_is_not()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await VerificationSupport.DrainCreditsAsync(fixture, owner.OrganizationId);

        var empty = await owner.CreditsAsync();
        empty.At("balance").GetDecimal().Should().Be(0m);
        empty.At("paused").GetBoolean().Should().BeTrue();

        await fixture.BuyCreditsAsync(owner, 10m);
        await WaitForBalanceAsync(owner, 10m);
        (await owner.CreditsAsync()).At("paused").GetBoolean().Should().BeFalse();
    }

    // ---- Auto-reload ----

    [Theory]
    [InlineData("card_declined")]
    [InlineData("authentication_required")]
    public async Task A_refused_auto_reload_marks_the_purchase_failed_turns_auto_reload_off_voids_the_invoice_and_does_not_throw(string code)
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await ArrangeAutoReloadAsync(owner.OrganizationId, customer: "cus_fictional_reload");
        using var stripe = new StripeStub(code);

        var outgoing = await ReloadAsync(owner.OrganizationId, StripeOptions(), stripe);

        outgoing.OfType<NotifyAiCredits>().Should().ContainSingle().Which.Should().Match<NotifyAiCredits>(n => n.Kind == CreditAlert.ReloadFailed && n.Amount == 25m);
        outgoing.OfType<GrantAiCredits>().Should().BeEmpty("a refused payment adds no credit");
        stripe.Paths.Should().Equal("/v1/invoices", "/v1/invoiceitems", "/v1/invoices/in_fictional/finalize", "/v1/invoices/in_fictional/pay", "/v1/invoices/in_fictional/void");
        await using var db = await VerificationSupport.TenantDbAsync(fixture, owner.OrganizationId);
        var purchase = await db.CreditPurchases.AsNoTracking().SingleAsync(Ct);
        purchase.Should().Match<CreditPurchase>(p => p.Status == CreditPurchaseStatus.Failed && p.FailureCode == code && p.Source == CreditPurchaseSource.AutoReload);
        var settings = await db.CreditSettings.AsNoTracking().SingleAsync(Ct);
        settings.AutoReloadEnabled.Should().BeFalse("a card that is refused is not tried again");
        settings.AutoReloadAmount.Should().Be(25m, "the amounts stay, so turning it on again is one switch");
        (await AiCreditLedger.BalanceMicrosAsync(db, Ct)).Should().Be(0);
    }

    [Fact]
    public async Task A_refused_auto_reload_tells_the_Owner_in_the_bell_and_by_email_though_the_organizations_budget_email_default_is_off()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        (await owner.GetAsync("/v1/organization/verification-settings")).Expect(HttpStatusCode.OK).At("alerts.budgetEmail").GetBoolean().Should().BeFalse();
        await ArrangeAutoReloadAsync(owner.OrganizationId, customer: "cus_fictional_reload");
        using var stripe = new StripeStub("card_declined");
        var outgoing = await ReloadAsync(owner.OrganizationId, StripeOptions(), stripe);
        var notify = outgoing.OfType<NotifyAiCredits>().Single();

        await PublishToWorkerAsync(notify);

        var bell = await owner.WaitForItemsAsync(1);
        bell.Text("items.0.kind").Should().Be("ai-credits-reload-failed");
        bell.Text("items.0.link").Should().Be(AiCreditWording.Path);
        var address = await EmailOfAsync(owner);
        var mails = await fixture.Mailpit.WaitForMessagesAsync(address, 1, Ct);
        mails.Should().Contain(m => m.Subject == AiCreditWording.Subject(CreditAlert.ReloadFailed));
        mails.Single(m => m.Subject == AiCreditWording.Subject(CreditAlert.ReloadFailed)).Text.Should().Contain("auto-reload is now off").And.Contain("$25.00");
    }

    [Fact]
    public async Task A_charge_for_an_organization_with_no_provider_customer_fails_the_purchase_and_tells_the_Owners_without_calling_the_provider()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await ArrangeAutoReloadAsync(owner.OrganizationId, customer: null);
        using var stripe = new StripeStub("card_declined");

        var outgoing = await ReloadAsync(owner.OrganizationId, StripeOptions(), stripe);

        outgoing.OfType<NotifyAiCredits>().Should().ContainSingle().Which.Kind.Should().Be(CreditAlert.ReloadFailed);
        stripe.Paths.Should().BeEmpty();
        await using var db = await VerificationSupport.TenantDbAsync(fixture, owner.OrganizationId);
        (await db.CreditPurchases.AsNoTracking().SingleAsync(Ct)).Status.Should().Be(CreditPurchaseStatus.Failed);
        (await db.CreditSettings.AsNoTracking().SingleAsync(Ct)).AutoReloadEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task A_sandbox_auto_reload_adds_its_amount_and_the_monthly_maximum_stops_the_next_one()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await ArrangeAutoReloadAsync(owner.OrganizationId, customer: null, monthlyMax: 25m);

        var first = await ReloadAsync(owner.OrganizationId, SandboxOptions(), null);

        var grant = first.OfType<GrantAiCredits>().Should().ContainSingle("the sandbox has no webhook, so the reload asks for its own grant").Subject;
        await GrantAsync(grant);
        await using (var db = await VerificationSupport.TenantDbAsync(fixture, owner.OrganizationId))
        {
            (await AiCreditLedger.BalanceMicrosAsync(db, Ct)).Should().Be(25_000_000);
            (await db.CreditEntries.AsNoTracking().SingleAsync(e => e.Kind == CreditEntryKind.AutoReload, Ct)).Micros.Should().Be(25_000_000);
            (await db.CreditPurchases.AsNoTracking().SingleAsync(Ct)).Status.Should().Be(CreditPurchaseStatus.Paid);
        }

        await VerificationSupport.DrainCreditsAsync(fixture, owner.OrganizationId);
        var second = await ReloadAsync(owner.OrganizationId, SandboxOptions(), null);

        second.Should().BeEmpty("25 charged this month and a maximum of 25 leave no room for another");
        await using var after = await VerificationSupport.TenantDbAsync(fixture, owner.OrganizationId);
        (await after.CreditPurchases.AsNoTracking().CountAsync(Ct)).Should().Be(1);
        (await AiCreditLedger.BalanceMicrosAsync(after, Ct)).Should().Be(0);
    }

    [Fact]
    public async Task The_running_worker_decides_charges_and_grants_an_auto_reload_in_the_sandbox()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await ArrangeAutoReloadAsync(owner.OrganizationId, customer: null);

        // The worker's own handler chain: the decision (deduplicated, in its transaction), the charge it queues, and the grant.
        await PublishToWorkerAsync(new ReloadAiCredits(owner.OrganizationId, Guid.CreateVersion7()));

        await WolverineStore.WaitUntilAsync(
            async () =>
            {
                await using var db = await VerificationSupport.TenantDbAsync(fixture, owner.OrganizationId);
                return await AiCreditLedger.BalanceMicrosAsync(db, Ct) == 25_000_000;
            },
            "the reload to be granted",
            Ct);
        await using var after = await VerificationSupport.TenantDbAsync(fixture, owner.OrganizationId);
        (await after.CreditPurchases.AsNoTracking().SingleAsync(Ct)).Should().Match<CreditPurchase>(p => p.Status == CreditPurchaseStatus.Paid && p.Source == CreditPurchaseSource.AutoReload);
    }

    [Fact]
    public async Task An_auto_reload_never_confirmed_in_a_day_is_closed_as_failed_turns_auto_reload_off_and_tells_the_Owners()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await ArrangeAutoReloadAsync(owner.OrganizationId, customer: null);
        await using (var db = await VerificationSupport.TenantDbAsync(fixture, owner.OrganizationId))
        {
            var waiting = new CreditPurchase { OrganizationId = owner.OrganizationId, Source = CreditPurchaseSource.AutoReload, Amount = 25m, CreatedAt = DateTimeOffset.UtcNow };
            waiting.RecordProviderPayment("in_fictional_waiting");
            db.CreditPurchases.Add(waiting);
            await db.SaveChangesAsync(Ct);
        }

        var meanwhile = await ReloadAsync(owner.OrganizationId, SandboxOptions(), null);
        meanwhile.Should().BeEmpty("a reload waiting for its confirmation is the reload, until the window has passed");

        var later = await ReloadAsync(owner.OrganizationId, SandboxOptions(), null, new FixedClock(DateTimeOffset.UtcNow + ReloadAiCreditsHandler.ConfirmationWindow + TimeSpan.FromMinutes(1)));

        later.OfType<NotifyAiCredits>().Should().ContainSingle().Which.Kind.Should().Be(CreditAlert.ReloadFailed);
        await using var after = await VerificationSupport.TenantDbAsync(fixture, owner.OrganizationId);
        (await after.CreditPurchases.AsNoTracking().SingleAsync(Ct)).Should().Match<CreditPurchase>(p => p.Status == CreditPurchaseStatus.Failed && p.FailureCode == "not-confirmed");
        (await after.CreditSettings.AsNoTracking().SingleAsync(Ct)).AutoReloadEnabled.Should().BeFalse();
    }

    // ---- Alerts ----

    [Fact]
    public async Task The_low_balance_alert_goes_out_once_for_a_refill_and_again_after_the_next()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await EnsureStartingCreditAsync(owner.OrganizationId);
        await DebitAsync(owner.OrganizationId, 2m);
        var due = await DueAsync(owner.OrganizationId);
        var low = due.OfType<NotifyAiCredits>().Should().ContainSingle().Subject;
        low.Kind.Should().Be(CreditAlert.Low);
        low.Amount.Should().Be(3m);

        // Delivered twice, as at-least-once delivery may: one bell line and one email.
        await PublishToWorkerAsync(low);
        await PublishToWorkerAsync(low);

        (await owner.WaitForItemsAsync(1)).Text("items.0.kind").Should().Be("ai-credits-low");
        var address = await EmailOfAsync(owner);
        await fixture.Mailpit.WaitForMessagesAsync(address, 1, Ct);
        await WolverineStore.WaitUntilAsync(async () => await AlertClaimedAsync(owner.OrganizationId, low.Key), "the alert to be claimed", Ct);
        await Wait.ForNothingAsync(TimeSpan.FromSeconds(2), "a second email for the same refill", Ct);
        (await fixture.Mailpit.MessagesAsync(address, Ct)).Where(m => m.Subject == AiCreditWording.Subject(CreditAlert.Low)).Should().ContainSingle();
        (await owner.NotificationsAsync()).Count("items").Should().Be(1);
        (await DueAsync(owner.OrganizationId)).Should().BeEmpty("the alert for this refill is claimed");

        // A refill, then the balance falls below the alert again: a new alert, under a new key.
        var purchase = await SeedPurchaseAsync(owner.OrganizationId, CreditPurchaseSource.Checkout, 10m);
        await GrantAsync(new GrantAiCredits(owner.OrganizationId, purchase.Id, "pi_fictional_refill"));
        await DebitAsync(owner.OrganizationId, 11m);
        var again = (await DueAsync(owner.OrganizationId)).OfType<NotifyAiCredits>().Should().ContainSingle().Subject;
        again.Kind.Should().Be(CreditAlert.Low);
        again.Key.Should().NotBe(low.Key);
    }

    [Fact]
    public async Task An_empty_balance_alerts_as_ran_out_and_a_reload_that_is_starting_alerts_nobody()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await VerificationSupport.DrainCreditsAsync(fixture, owner.OrganizationId);

        (await DueAsync(owner.OrganizationId)).OfType<NotifyAiCredits>().Should().ContainSingle().Which.Kind.Should().Be(CreditAlert.Empty);

        await ArrangeAutoReloadAsync(owner.OrganizationId, customer: null);
        var starting = await DueAsync(owner.OrganizationId);
        starting.OfType<ReloadAiCredits>().Should().ContainSingle("the balance is below the threshold, so a reload starts");
        starting.OfType<NotifyAiCredits>().Should().BeEmpty("the reload is the remedy, so no alert goes out while it starts");
    }

    // ---- Expiry ----

    [Fact]
    public async Task Expiry_writes_off_what_is_left_of_each_grant_once_and_the_deductions_list_it()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var granted = Future.AddMonths(-12).AddDays(-30);
        var grant = new CreditEntry
        {
            OrganizationId = owner.OrganizationId, Kind = CreditEntryKind.Purchase, Micros = 10_000_000, At = granted, ExpiresAt = Future.AddDays(-30), SourceKey = "purchase:expiry-test",
        };
        await using (var db = await VerificationSupport.TenantDbAsync(fixture, owner.OrganizationId))
        {
            db.CreditEntries.AddRange(
                grant,
                new CreditEntry { OrganizationId = owner.OrganizationId, Kind = CreditEntryKind.Usage, Micros = -4_000_000, At = granted.AddMonths(1), SourceKey = "call:expiry-test" });
            await db.SaveChangesAsync(Ct);
        }

        await ExpireAsync(owner.OrganizationId, Future, "2099-06-15");
        await ExpireAsync(owner.OrganizationId, Future.AddDays(1), "2099-06-16");

        await using var after = await VerificationSupport.TenantDbAsync(fixture, owner.OrganizationId);
        var expiries = await after.CreditEntries.AsNoTracking().Where(e => e.Kind == CreditEntryKind.Expiry).ToListAsync(Ct);
        // The starting credit every organization got at sign-up lasted a year too, so it expired as well: five dollars, unspent.
        expiries.Select(e => e.Micros).Should().BeEquivalentTo([-5_000_000L, -6_000_000L], "each grant is written off once, though the sweep ran twice");
        expiries.Single(e => e.GrantEntryId == grant.Id).Micros.Should().Be(-6_000_000, "10 granted, 4 spent");
        (await AiCreditLedger.BalanceMicrosAsync(after, Ct)).Should().Be(0);
        var deductions = (await owner.GetAsync($"{BillingTestSupport.Credits}/deductions")).Expect(HttpStatusCode.OK);
        deductions.At("deductions").EnumerateArray().Where(d => d.GetProperty("kind").GetString() == "Expiry").Select(d => d.GetProperty("amount").GetDecimal())
            .Should().BeEquivalentTo([5m, 6m]);
    }

    // ---- Who may do what ----

    [Fact]
    public async Task Owners_and_Admins_read_the_balance_and_deductions_only_Owners_buy_and_see_purchases_and_a_Recruiter_or_a_key_is_refused()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var admin = await fixture.InviteAndJoinAsync(owner, "Admin");
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        var settings = new JsonObject { ["lowBalanceAlert"] = 5, ["autoReload"] = new JsonObject { ["enabled"] = false } };
        var checkout = new JsonObject { ["amount"] = 10 };

        foreach (var reader in new[] { owner, admin })
        {
            (await reader.GetAsync(BillingTestSupport.Credits)).Expect(HttpStatusCode.OK);
            (await reader.GetAsync($"{BillingTestSupport.Credits}/deductions")).Expect(HttpStatusCode.OK);
        }

        (await owner.GetAsync($"{BillingTestSupport.Credits}/purchases")).Expect(HttpStatusCode.OK);
        (await owner.PutAsync($"{BillingTestSupport.Credits}/settings", settings)).Expect(HttpStatusCode.NoContent);
        (await admin.GetAsync($"{BillingTestSupport.Credits}/purchases")).Expect(HttpStatusCode.Forbidden);
        (await admin.PostAsync($"{BillingTestSupport.Credits}/checkout", checkout)).Expect(HttpStatusCode.Forbidden);
        (await admin.PutAsync($"{BillingTestSupport.Credits}/settings", settings)).Expect(HttpStatusCode.Forbidden);
        (await recruiter.GetAsync(BillingTestSupport.Credits)).Expect(HttpStatusCode.Forbidden);
        (await recruiter.GetAsync($"{BillingTestSupport.Credits}/deductions")).Expect(HttpStatusCode.Forbidden);
        (await recruiter.GetAsync($"{BillingTestSupport.Credits}/purchases")).Expect(HttpStatusCode.Forbidden);
        (await recruiter.PostAsync($"{BillingTestSupport.Credits}/checkout", checkout)).Expect(HttpStatusCode.Forbidden);
        (await recruiter.PutAsync($"{BillingTestSupport.Credits}/settings", settings)).Expect(HttpStatusCode.Forbidden);

        using var key = new KeyClient(fixture, (await owner.CreateKeyAsync("ATS sync", ["Read", "Write"])).Text("token"));
        (await key.GetAsync(BillingTestSupport.Credits)).Status.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
        (await key.PostAsync($"{BillingTestSupport.Credits}/checkout", checkout)).Status.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.NotFound);

        using var anonymous = fixture.CreateHttpClient("api");
        (await anonymous.GetAsync(BillingTestSupport.Credits, Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_credit_checkout_for_an_amount_that_is_not_offered_is_refused_and_leaves_no_purchase()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();

        (await owner.PostAsync($"{BillingTestSupport.Credits}/checkout", new JsonObject { ["amount"] = 7 })).Expect(HttpStatusCode.BadRequest)
            .ProblemKeys().Should().Equal("amount");
        (await owner.PostAsync($"{BillingTestSupport.Credits}/checkout", new JsonObject { ["amount"] = 0.01 })).Expect(HttpStatusCode.BadRequest);

        await using var db = await VerificationSupport.TenantDbAsync(fixture, owner.OrganizationId);
        (await db.CreditPurchases.AsNoTracking().CountAsync(Ct)).Should().Be(0);
        (await owner.CreditsAsync()).At("amounts").EnumerateArray().Select(a => a.GetDecimal()).Should().Equal(10m, 25m, 50m, 100m);
    }

    [Fact]
    public async Task Auto_reload_cannot_be_turned_on_without_a_saved_card_and_can_once_a_purchase_saved_one()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var on = new JsonObject
        {
            ["lowBalanceAlert"] = 5,
            ["autoReload"] = new JsonObject { ["enabled"] = true, ["below"] = 5, ["amount"] = 25, ["monthlyMax"] = 100 },
        };

        (await owner.PutAsync($"{BillingTestSupport.Credits}/settings", on)).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("autoReload.enabled");
        (await owner.CreditsAsync()).At("autoReload.enabled").GetBoolean().Should().BeFalse();

        await fixture.BuyCreditsAsync(owner, 10m);
        await WaitForBalanceAsync(owner, StartingCredit + 10m);
        (await owner.PutAsync($"{BillingTestSupport.Credits}/settings", on)).Expect(HttpStatusCode.NoContent);

        var saved = (await owner.CreditsAsync()).At("autoReload");
        saved.GetProperty("enabled").GetBoolean().Should().BeTrue();
        saved.GetProperty("below").GetDecimal().Should().Be(5m);
        saved.GetProperty("amount").GetDecimal().Should().Be(25m);
        saved.GetProperty("monthlyMax").GetDecimal().Should().Be(100m);
        var tooSmall = new JsonObject { ["lowBalanceAlert"] = 5, ["autoReload"] = new JsonObject { ["enabled"] = true, ["below"] = 5, ["amount"] = 25, ["monthlyMax"] = 10 } };
        (await owner.PutAsync($"{BillingTestSupport.Credits}/settings", tooSmall)).Expect(HttpStatusCode.BadRequest)
            .ProblemKeys().Should().Contain("autoReload.monthlyMax", "a maximum below one reload would never allow one");
    }

    [Fact]
    public async Task Another_organizations_credits_are_never_read_or_written()
    {
        using var ours = await fixture.SignedInWithOrganizationAsync();
        using var theirs = await fixture.SignedInWithOrganizationAsync();
        await fixture.BuyCreditsAsync(ours, 50m);
        await WaitForBalanceAsync(ours, StartingCredit + 50m);

        (await theirs.CreditsAsync()).At("balance").GetDecimal().Should().Be(StartingCredit);
        (await theirs.GetAsync($"{BillingTestSupport.Credits}/purchases")).Expect(HttpStatusCode.OK).Count("purchases").Should().Be(1);
        await using var outside = await VerificationSupport.TenantDbAsync(fixture, theirs.OrganizationId);
        (await outside.CreditEntries.IgnoreQueryFilters().CountAsync(e => e.Micros == 50_000_000, Ct)).Should().Be(0, "row-level security hides the row even without the query filter");
        outside.CreditEntries.Add(new CreditEntry { OrganizationId = ours.OrganizationId, Kind = CreditEntryKind.Purchase, Micros = 1, At = Future, SourceKey = "purchase:foreign" });
        await FluentActions.Awaiting(() => outside.SaveChangesAsync(Ct)).Should().ThrowAsync<DbUpdateException>("the policy refuses a row for another organization");
        await using var edit = await VerificationSupport.TenantDbAsync(fixture, ours.OrganizationId);
        await FluentActions.Awaiting(() => edit.Database.ExecuteSqlRawAsync("UPDATE credit_entries SET micros = 1", Ct)).Should()
            .ThrowAsync<Exception>("the ledger is append-only for the application role");
        await FluentActions.Awaiting(() => edit.Database.ExecuteSqlRawAsync("DELETE FROM credit_entries", Ct)).Should().ThrowAsync<Exception>();
    }

    // ---- The Billing page's month ----

    [Fact]
    public async Task The_overview_counts_this_months_built_in_calls_at_cost_plus_the_margin_and_leaves_own_key_and_unknown_cost_uncharged()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var now = DateTimeOffset.UtcNow;
        await using (var db = await VerificationSupport.TenantDbAsync(fixture, owner.OrganizationId))
        {
            db.AiUsageCalls.AddRange(
                Call(owner.OrganizationId, now.AddSeconds(-30), 1.00m),
                Call(owner.OrganizationId, now.AddSeconds(-20), 0.0004m),
                Call(owner.OrganizationId, now.AddSeconds(-10), null),
                Call(owner.OrganizationId, now.AddSeconds(-5), 9.00m, AiKeySource.OwnKey));
            await db.SaveChangesAsync(Ct);
        }

        var billing = await owner.BillingAsync();
        var usage = billing.At("aiUsage");

        usage.GetProperty("builtInCalls").GetInt32().Should().Be(3, "the call with an unknown cost is counted");
        usage.GetProperty("charge").GetDecimal().Should().Be(1.20048m, "each call's cost plus 20%, rounded to a micro-dollar, then summed");
        usage.TryGetProperty("providerCost", out _).Should().BeFalse("a customer sees one amount for AI use, never the provider's price");
        usage.TryGetProperty("marginPercent", out _).Should().BeFalse("and never the margin");
        usage.GetProperty("callsWithUnknownCost").GetInt32().Should().Be(1);
        usage.GetProperty("ownKeyCalls").GetInt32().Should().Be(1);
        usage.GetProperty("month").GetString().Should().Be(now.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture));

        // On the trial the plan costs nothing and no credits were bought, so nothing is owed this month and no invoice is coming.
        var month = billing.At("thisMonth");
        month.GetProperty("planAmount").GetDecimal().Should().Be(0m);
        month.GetProperty("creditsBought").GetDecimal().Should().Be(0m, "the starting credit is given, not bought");
        month.GetProperty("total").GetDecimal().Should().Be(0m);
        month.GetProperty("nextInvoiceAmount").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // ---- Support ----

    private static BillingOptions SandboxOptions() => new() { Sandbox = new BillingSandboxOptions { Enabled = true } };

    private static BillingOptions StripeOptions() => new() { SecretKey = "sk_test_fictional_key", WebhookSecret = "whsec_fictional_secret" };

    private static async Task WaitForBalanceAsync(Tenant owner, decimal balance) =>
        await WolverineStore.WaitUntilAsync(
            async () => (await owner.CreditsAsync()).At("balance").GetDecimal() == balance, $"the balance to reach {balance}", Ct);

    private async Task EnsureStartingCreditAsync(Guid organizationId)
    {
        await using var db = await VerificationSupport.TenantDbAsync(fixture, organizationId);
        await AiCreditLedger.EnsureStartingCreditAsync(db, organizationId, SandboxOptions(), DateTimeOffset.UtcNow, Ct);
    }

    private async Task<CreditPurchase> SeedPurchaseAsync(Guid organizationId, CreditPurchaseSource source, decimal amount)
    {
        await using var db = await VerificationSupport.TenantDbAsync(fixture, organizationId);
        var purchase = new CreditPurchase { OrganizationId = organizationId, Source = source, Amount = amount, CreatedAt = DateTimeOffset.UtcNow };
        db.CreditPurchases.Add(purchase);
        await db.SaveChangesAsync(Ct);
        return purchase;
    }

    // A debit of <dollars> without a call behind it: what a check leaves, as far as the balance is concerned.
    private async Task DebitAsync(Guid organizationId, decimal dollars)
    {
        await using var db = await VerificationSupport.TenantDbAsync(fixture, organizationId);
        db.CreditEntries.Add(new CreditEntry
        {
            OrganizationId = organizationId,
            Kind = CreditEntryKind.Usage,
            Micros = -(long)(dollars * 1_000_000m),
            At = DateTimeOffset.UtcNow,
            SourceKey = CreditSourceKeys.Call(Guid.CreateVersion7()),
        });
        await db.SaveChangesAsync(Ct);
    }

    // The organization ready to reload: no credit left, a saved card, auto-reload on below 5 for 25, and optionally a customer at the provider.
    private async Task ArrangeAutoReloadAsync(Guid organizationId, string? customer, decimal? monthlyMax = 100m)
    {
        await VerificationSupport.DrainCreditsAsync(fixture, organizationId);
        await using var db = await VerificationSupport.TenantDbAsync(fixture, organizationId);
        var now = DateTimeOffset.UtcNow;
        if (customer is not null)
        {
            (await db.BillingAccounts.SingleAsync(Ct)).AdoptCustomer(customer, now);
        }

        var settings = await AiCreditLedger.SettingsAsync(db, organizationId, SandboxOptions(), now, Ct);
        settings.SetAutoReload(true, 5m, 25m, monthlyMax, now);
        settings.SaveCard("pm_fictional_reload", now);
        await db.SaveChangesAsync(Ct);
    }

    private async Task<List<object>> DueAsync(Guid organizationId)
    {
        await using var db = await VerificationSupport.TenantDbAsync(fixture, organizationId);
        return await AiCreditChecks.DueAsync(db, organizationId, SandboxOptions(), DateTimeOffset.UtcNow, Ct);
    }

    private async Task<bool> AlertClaimedAsync(Guid organizationId, string key)
    {
        await using var db = await VerificationSupport.TenantDbAsync(fixture, organizationId);
        return await db.AiUsageAlerts.AsNoTracking().AnyAsync(a => a.Key == key, Ct);
    }

    private async Task<OutgoingMessages> GrantAsync(GrantAiCredits message)
    {
        var options = SandboxOptions();
        using var providers = new Providers(options, null);
        await using var db = await VerificationSupport.TenantDbAsync(fixture, message.OrganizationId);
        return await GrantAiCreditsHandler.Handle(
            message, db, providers.Access, Microsoft.Extensions.Options.Options.Create(options), TimeProvider.System, NullLogger<GrantAiCredits>.Instance, Ct);
    }

    // Both steps of an auto-reload, as the worker runs them: the decision, then the charge it queued. What both send, but the charge itself.
    private async Task<List<object>> ReloadAsync(Guid organizationId, BillingOptions options, HttpMessageHandler? stripe, TimeProvider? clock = null)
    {
        using var providers = new Providers(options, stripe);
        var time = clock ?? TimeProvider.System;
        OutgoingMessages decided;
        await using (var db = await VerificationSupport.TenantDbAsync(fixture, organizationId))
        {
            decided = await ReloadAiCreditsHandler.Handle(
                new ReloadAiCredits(organizationId, Guid.CreateVersion7()), db, providers.Access, Microsoft.Extensions.Options.Options.Create(options),
                time, NullLogger<ReloadAiCredits>.Instance, Ct);
        }

        var sent = decided.Where(m => m is not ChargeAiCreditReload).ToList();
        foreach (var charge in decided.OfType<ChargeAiCreditReload>())
        {
            await using var db = await VerificationSupport.TenantDbAsync(fixture, organizationId);
            sent.AddRange(await ChargeAiCreditReloadHandler.Handle(charge, db, providers.Access, time, NullLogger<ChargeAiCreditReload>.Instance, Ct));
        }

        return sent;
    }

    private async Task ExpireAsync(Guid organizationId, DateTimeOffset now, string day)
    {
        var options = SandboxOptions();
        await using var db = await VerificationSupport.TenantDbAsync(fixture, organizationId);
        await ExpireOrganizationAiCreditsHandler.Handle(
            new ExpireOrganizationAiCredits(organizationId, day), db, Microsoft.Extensions.Options.Options.Create(options), new FixedClock(now),
            NullLogger<ExpireOrganizationAiCredits>.Instance, Ct);
    }

    // Puts a message on the real worker's queue, as the API or another handler would.
    private async Task PublishToWorkerAsync(object message)
    {
        await using var publisher = await MessagingHost.StartPublisherToAppHostQueuesAsync(fixture, Ct);
        await publisher.Services.GetRequiredService<IMessageBus>().PublishAsync(message);
    }

    private async Task<string> EmailOfAsync(Tenant member)
    {
        await using var db = await VerificationSupport.TenantDbAsync(fixture, member.OrganizationId);
        return await db.Users.AsNoTracking().Where(u => u.Id == member.UserId).Select(u => u.Email).SingleAsync(Ct) ?? throw new InvalidOperationException("The signed-in user has no email address.");
    }

    // Applies once with the consistency check on (for an own key, the settings given), and waits for every analyzer to run.
    private async Task ApplyWithCheckAsync(Tenant tenant, JsonObject? settings)
    {
        (await tenant.PutAsync("/v1/organization/verification-settings", settings ?? new JsonObject { ["llmConsistencyCheck"] = true })).Expect(HttpStatusCode.OK);
        var (slug, jobId) = await VerificationSupport.OpenJobAsync(fixture, tenant, SystemTemplates.SoftwareEngineerVersion2);
        var applicant = SyntheticApplicants.GenuineHuman;
        var applicationId = await VerificationSupport.ApplyAsync(fixture, slug, jobId, applicant, answers: applicant.Answers);
        (await VerificationSupport.SettledAsync(fixture, tenant, applicationId)).Text("consistencyCheck.status").Should().Be("Completed");
    }

    private static AiUsageCall Call(Guid organizationId, DateTimeOffset at, decimal? cost, AiKeySource key = AiKeySource.BuiltIn) => new()
    {
        OrganizationId = organizationId,
        OccurredAt = at,
        CheckName = AiUsageChecks.ConsistencyComparison,
        Provider = "Fake",
        Model = FakeConsistencyChatClient.ModelId,
        KeySource = key,
        ProviderRequestId = $"seed-{Guid.NewGuid():N}",
        InputTokens = 1000,
        OutputTokens = 100,
        Cost = cost,
        CostBasis = cost is null ? AiCostBasis.Unknown : AiCostBasis.Priced,
        Outcome = AiCallOutcome.Ok,
    };

    // Both providers behind BillingAccess for the mode the options give, the Stripe one over a stand-in for Stripe's servers.
    private sealed class Providers : IDisposable
    {
        private readonly StripeBillingProvider stripe;

        public Providers(BillingOptions options, HttpMessageHandler? handler)
        {
            var settings = Microsoft.Extensions.Options.Options.Create(options);
            stripe = new StripeBillingProvider(settings, new PlanCatalogue(settings), handler ?? new StripeStub("card_declined"));
            Access = new BillingAccess(settings, stripe, new SandboxBillingProvider(new EphemeralDataProtectionProvider(), TimeProvider.System, settings));
        }

        public BillingAccess Access { get; }

        public void Dispose() => stripe.Dispose();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // Stripe's servers for an auto-reload: the draft invoice, its item and its finalize are taken, paying it is refused with <code>
    // (402), and voiding is taken. Every path is kept in the order it was called.
    private sealed class StripeStub(string code) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            var response = path.EndsWith("/pay", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.PaymentRequired) { Content = new StringContent($$$"""{"error":{"type":"card_error","code":"{{{code}}}"}}""") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"id":"in_fictional","status":"open"}""") };
            return Task.FromResult(response);
        }
    }
}
