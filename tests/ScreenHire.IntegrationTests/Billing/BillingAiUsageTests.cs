using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Billing;
using ScreenHire.Application.Messaging;
using ScreenHire.Domain.Billing;
using ScreenHire.Domain.Verification;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using ScreenHire.Infrastructure.Verification.Llm;
using ScreenHire.IntegrationTests.Messaging;
using Wolverine;
using Wolverine.Runtime.Recurring;

namespace ScreenHire.IntegrationTests.Billing;

/// <summary>
/// Reporting AI use on ScreenHire's key to the payment provider's meter (docs/architecture/billing.md, "AI usage"): which ledger
/// calls are settled, once each, in what amount; the trial's allowance; a batch the provider stops part-way; and what the Billing
/// overview says about the month. The handler runs in process against the stack's database with a fixed clock far in the future, so the
/// real worker's own runs (which never look past their own now) cannot settle the rows a test seeds.
/// </summary>
public sealed class BillingAiUsageTests(AppHostFixture fixture)
{
    // Far in the future, like the ledger's tests, so the real worker's 15-minute runs never reach rows these tests seed.
    private static readonly DateTimeOffset Future = new(2099, 6, 15, 6, 7, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- What is settled, and how ----

    [Fact]
    public async Task Only_built_in_calls_with_a_known_cost_are_reported_each_once_at_the_providers_price_plus_the_margin()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await ActivateAsync(owner.OrganizationId, trialEndsAt: Future.AddDays(-10));
        var priced = new[] { Priced(owner, Future.AddMinutes(-30), 1.00m), Priced(owner, Future.AddMinutes(-20), 0.0004m), Priced(owner, Future.AddMinutes(-10), 3.12m) };
        var ownKey = Priced(owner, Future.AddMinutes(-15), 9.00m, AiKeySource.OwnKey);
        var unknown = Unpriced(owner, Future.AddMinutes(-12));
        var stale = Priced(owner, Future.AddDays(-40), 7.00m);
        await SeedAsync(owner.OrganizationId, [.. priced, ownKey, unknown, stale]);

        await ReportAsync(owner.OrganizationId, SandboxOptions(), Future);

        var charges = await ChargesAsync(owner.OrganizationId);
        charges.Select(c => c.Id).Should().BeEquivalentTo(priced.Select(c => c.Id), "an own-key call, a call with no known cost and one past the window are never reported");
        charges.Should().AllSatisfy(c =>
        {
            c.State.Should().Be(AiChargeState.Reported);
            c.Provider.Should().Be(AiChargeProvider.Sandbox);
            c.Margin.Should().Be(0.20m);
            c.SettledAt.Should().Be(Future);
        });
        charges.OrderBy(c => c.Micros).Select(c => c.Micros).Should().Equal(480, 1_200_000, 3_744_000);

        await ReportAsync(owner.OrganizationId, SandboxOptions(), Future.AddMinutes(15));
        (await ChargesAsync(owner.OrganizationId)).Should().HaveCount(3, "a second run finds every call settled and reports nothing again");
    }

    [Fact]
    public async Task A_call_that_cost_nothing_is_settled_as_waived_and_never_sent()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await ActivateAsync(owner.OrganizationId, trialEndsAt: Future.AddDays(-10));
        var free = Priced(owner, Future.AddMinutes(-5), 0m);
        await SeedAsync(owner.OrganizationId, [free]);
        using var stripe = new StubStripe();

        await ReportAsync(owner.OrganizationId, StripeOptions(), Future, stripe: stripe);

        (await ChargesAsync(owner.OrganizationId)).Should().ContainSingle().Which.Should().Match<AiUsageCharge>(c => c.State == AiChargeState.Waived && c.Micros == 0);
        stripe.Identifiers.Should().BeEmpty("the meter takes positive values only");
    }

    [Fact]
    public async Task The_trial_allowance_covers_the_first_calls_in_time_order_and_the_rest_is_reported()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await ActivateAsync(owner.OrganizationId, trialEndsAt: Future.AddDays(5));
        // 2.00 plus 20% is 2.40 each: two fit in a 5.00 allowance (4.80), the third does not.
        var calls = new[] { Priced(owner, Future.AddMinutes(-30), 2.00m), Priced(owner, Future.AddMinutes(-20), 2.00m), Priced(owner, Future.AddMinutes(-10), 2.00m) };
        await SeedAsync(owner.OrganizationId, calls);

        await ReportAsync(owner.OrganizationId, SandboxOptions(), Future);

        var charges = (await ChargesAsync(owner.OrganizationId)).ToDictionary(c => c.Id);
        charges[calls[0].Id].State.Should().Be(AiChargeState.Waived);
        charges[calls[1].Id].State.Should().Be(AiChargeState.Waived);
        charges[calls[2].Id].State.Should().Be(AiChargeState.Reported);
        charges.Values.Should().OnlyContain(c => c.Micros == 2_400_000);
    }

    [Fact]
    public async Task With_no_allowance_a_trial_call_is_reported_in_full_once_a_plan_is_paying()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await ActivateAsync(owner.OrganizationId, trialEndsAt: Future.AddDays(5));
        var call = Priced(owner, Future.AddMinutes(-10), 1.00m);
        await SeedAsync(owner.OrganizationId, [call]);
        var options = SandboxOptions();
        options.Ai.TrialAllowance = 0m;

        await ReportAsync(owner.OrganizationId, options, Future);

        (await ChargesAsync(owner.OrganizationId)).Should().ContainSingle().Which.State.Should().Be(AiChargeState.Reported);
    }

    [Theory]
    [InlineData(SubscriptionStatus.None)]
    [InlineData(SubscriptionStatus.Ended)]
    public async Task Nothing_is_reported_before_a_plan_starts_or_after_it_ended_and_the_calls_wait(SubscriptionStatus status)
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await ActivateAsync(owner.OrganizationId, trialEndsAt: Future.AddDays(-10), status: status);
        await SeedAsync(owner.OrganizationId, [Priced(owner, Future.AddMinutes(-10), 1.00m)]);

        await ReportAsync(owner.OrganizationId, SandboxOptions(), Future);
        (await ChargesAsync(owner.OrganizationId)).Should().BeEmpty();

        // A plan starting later settles what waited.
        await ActivateAsync(owner.OrganizationId, trialEndsAt: Future.AddDays(-10), status: SubscriptionStatus.Active);
        await ReportAsync(owner.OrganizationId, SandboxOptions(), Future.AddMinutes(15));
        (await ChargesAsync(owner.OrganizationId)).Should().ContainSingle();
    }

    [Fact]
    public async Task A_full_batch_asks_for_the_next_one_and_every_call_is_settled_once_across_the_batches()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await ActivateAsync(owner.OrganizationId, trialEndsAt: Future.AddDays(-10));
        var calls = Enumerable.Range(1, 5).Select(i => Priced(owner, Future.AddMinutes(-i), 0.10m)).ToArray();
        await SeedAsync(owner.OrganizationId, calls);
        var options = SandboxOptions();
        options.Ai.ReportBatchSize = 2;

        var runs = 0;
        ReportOrganizationAiUsage? next = null;
        do
        {
            var outgoing = await ReportAsync(owner.OrganizationId, options, Future, slot: "slot", after: next?.After);
            next = outgoing.OfType<ReportOrganizationAiUsage>().SingleOrDefault();
            runs++;
        }
        while (next is not null && runs < 10);

        runs.Should().Be(3, "two, two and one");
        (await ChargesAsync(owner.OrganizationId)).Select(c => c.Id).Should().BeEquivalentTo(calls.Select(c => c.Id));
    }

    [Fact]
    public async Task The_follow_up_for_a_full_batch_has_a_key_of_its_own_so_it_is_not_swallowed_as_a_repeat_of_the_slot()
    {
        var org = Guid.CreateVersion7();
        var first = new ReportOrganizationAiUsage(org, "42");
        var follow = first with { After = Guid.CreateVersion7() };

        follow.IdempotencyKey.Should().NotBe(first.IdempotencyKey);
        first.IdempotencyKey.Should().Be($"ai-report:{org:N}:42");
    }

    // ---- Stripe: a batch the provider stops part-way, and an account it cannot bill ----

    [Fact]
    public async Task A_batch_stripe_stops_part_way_keeps_what_it_took_and_the_next_run_sends_only_the_rest()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await ActivateAsync(owner.OrganizationId, trialEndsAt: Future.AddDays(-10));
        var calls = new[] { Priced(owner, Future.AddMinutes(-30), 1m), Priced(owner, Future.AddMinutes(-20), 1m), Priced(owner, Future.AddMinutes(-10), 1m) };
        await SeedAsync(owner.OrganizationId, calls);
        using var stripe = new StubStripe(acceptFirst: 2);

        var outgoing = await ReportAsync(owner.OrganizationId, StripeOptions(), Future, stripe: stripe);

        outgoing.OfType<ReportOrganizationAiUsage>().Should().BeEmpty("the rest waits for the next slot");
        var kept = await ChargesAsync(owner.OrganizationId);
        kept.Select(c => c.Id).Should().BeEquivalentTo(calls.Take(2).Select(c => c.Id), "what Stripe took is saved, so a retry cannot send it again");
        kept.Should().OnlyContain(c => c.Provider == AiChargeProvider.Stripe && c.State == AiChargeState.Reported);
        stripe.Identifiers.Should().Equal(calls.Select(c => c.Id.ToString()).Take(3), "the third was sent and refused");

        stripe.Reset(acceptFirst: int.MaxValue);
        await ReportAsync(owner.OrganizationId, StripeOptions(), Future.AddMinutes(15), stripe: stripe);

        stripe.Identifiers.Should().Equal([calls[2].Id.ToString()], "only the call Stripe had not taken is sent again");
        (await ChargesAsync(owner.OrganizationId)).Should().HaveCount(3);
    }

    [Fact]
    public async Task The_stripe_event_carries_the_customer_the_meter_name_the_ledger_id_and_the_charge_in_micro_dollars()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await ActivateAsync(owner.OrganizationId, trialEndsAt: Future.AddDays(-10), customer: "cus_fictional_meter");
        var call = Priced(owner, Future.AddMinutes(-10), 3.12m);
        await SeedAsync(owner.OrganizationId, [call]);
        using var stripe = new StubStripe();
        var options = StripeOptions();
        options.Ai.MeterEventName = "ai_usage_fictional";

        await ReportAsync(owner.OrganizationId, options, Future, stripe: stripe);

        var form = stripe.Forms.Should().ContainSingle().Subject;
        form["event_name"].Should().Be("ai_usage_fictional");
        form["identifier"].Should().Be(call.Id.ToString());
        form["payload[stripe_customer_id]"].Should().Be("cus_fictional_meter");
        form["payload[value]"].Should().Be("3744000");
    }

    [Fact]
    public async Task Stripe_with_no_metered_price_or_no_customer_is_sent_nothing_and_the_calls_wait()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await ActivateAsync(owner.OrganizationId, trialEndsAt: Future.AddDays(-10), customer: null);
        await SeedAsync(owner.OrganizationId, [Priced(owner, Future.AddMinutes(-10), 1m)]);
        using var stripe = new StubStripe();

        await ReportAsync(owner.OrganizationId, StripeOptions(), Future, stripe: stripe);
        var noPrice = StripeOptions();
        noPrice.Ai.StripePriceId = null;
        await ActivateAsync(owner.OrganizationId, trialEndsAt: Future.AddDays(-10), customer: "cus_fictional");
        await ReportAsync(owner.OrganizationId, noPrice, Future, stripe: stripe);

        stripe.Identifiers.Should().BeEmpty();
        (await ChargesAsync(owner.OrganizationId)).Should().BeEmpty();
    }

    [Fact]
    public async Task With_billing_off_nothing_is_reported()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await ActivateAsync(owner.OrganizationId, trialEndsAt: Future.AddDays(-10));
        await SeedAsync(owner.OrganizationId, [Priced(owner, Future.AddMinutes(-10), 1m)]);

        await ReportAsync(owner.OrganizationId, new BillingOptions(), Future);

        (await ChargesAsync(owner.OrganizationId)).Should().BeEmpty();
    }

    // ---- Isolation and the schedule ----

    [Fact]
    public async Task A_settled_call_stays_with_its_organization_and_cannot_be_edited_or_settled_for_another()
    {
        using var ours = await fixture.SignedInWithOrganizationAsync();
        using var theirs = await fixture.SignedInWithOrganizationAsync();
        await ActivateAsync(ours.OrganizationId, trialEndsAt: Future.AddDays(-10));
        await SeedAsync(ours.OrganizationId, [Priced(ours, Future.AddMinutes(-10), 1m)]);
        await ReportAsync(ours.OrganizationId, SandboxOptions(), Future);

        await using (var outside = await VerificationSupport.TenantDbAsync(fixture, theirs.OrganizationId))
        {
            (await outside.AiUsageCharges.IgnoreQueryFilters().CountAsync(Ct)).Should().Be(0, "row-level security hides it even without the query filter");
            outside.AiUsageCharges.Add(new AiUsageCharge { Id = Guid.CreateVersion7(), OrganizationId = ours.OrganizationId, Micros = 1, Margin = 0.2m, SettledAt = Future });
            await FluentActions.Awaiting(() => outside.SaveChangesAsync(Ct)).Should().ThrowAsync<DbUpdateException>("the policy refuses a row for another organization");
        }

        await using var edit = await VerificationSupport.TenantDbAsync(fixture, ours.OrganizationId);
        var update = async () => await edit.Database.ExecuteSqlRawAsync("UPDATE ai_usage_charges SET micros = 0", Ct);
        await update.Should().ThrowAsync<Exception>("a settled call is never edited, so it cannot be reported twice");
    }

    [Fact]
    public async Task The_fan_out_asks_every_organization_with_the_slot_it_was_asked_in()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await using var db = await fixture.CreateDbContextAsync(new TenantContext(), Ct);

        var outgoing = await ReportAiUsageHandler.Handle(new ReportAiUsage(), db, new FixedClock(Future), Ct);

        var asks = outgoing.OfType<ReportOrganizationAiUsage>().ToList();
        asks.Should().Contain(a => a.OrganizationId == owner.OrganizationId);
        asks.Select(a => a.Slot).Distinct().Should().ContainSingle().Which.Should().Be((Future.UtcTicks / TimeSpan.FromMinutes(15).Ticks).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Reporting_is_a_tracked_schedule_every_fifteen_minutes()
    {
        var settings = new MessagingSettings { Schema = MessagingHost.NewSchema(), AutoProvision = true };
        await using var worker = await MessagingHost.StartHandlerAsync(fixture, settings, [], Ct, withRecurringSchedules: true);
        var schedules = worker.Services.GetRequiredService<IRecurringScheduleControl>();

        await WolverineStore.WaitUntilAsync(
            async () => (await schedules.QueryAsync(Ct)).Any(s => s.Name == nameof(ReportAiUsage)), "the AI usage report schedule to be tracked", Ct);

        (await schedules.QueryAsync(Ct)).Single(s => s.Name == nameof(ReportAiUsage)).CronExpression.Should().Be("*/15 * * * *");
    }

    // ---- The Billing page's month ----

    [Fact]
    public async Task The_overview_counts_this_months_built_in_calls_prices_them_with_the_margin_and_leaves_own_key_and_unknown_cost_uncharged()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(owner.OrganizationId,
        [
            Priced(owner, now.AddSeconds(-30), 1.00m),
            Priced(owner, now.AddSeconds(-20), 0.0004m),
            Unpriced(owner, now.AddSeconds(-10)),
            Priced(owner, now.AddSeconds(-5), 9.00m, AiKeySource.OwnKey),
        ]);

        var usage = (await owner.BillingAsync()).At("aiUsage");

        usage.GetProperty("builtInCalls").GetInt32().Should().Be(3, "the call with an unknown cost is counted");
        usage.GetProperty("providerCost").GetDecimal().Should().Be(1.0004m);
        usage.GetProperty("charge").GetDecimal().Should().Be(1.20048m, "each call's cost plus 20%, rounded to a micro-dollar, then summed");
        usage.GetProperty("marginPercent").GetDecimal().Should().Be(20m);
        usage.GetProperty("callsWithUnknownCost").GetInt32().Should().Be(1);
        usage.GetProperty("ownKeyCalls").GetInt32().Should().Be(1);
        usage.GetProperty("trialAllowance").GetDecimal().Should().Be(5m);
        usage.GetProperty("trialAllowanceUsed").GetDecimal().Should().Be(0m);
        usage.GetProperty("metered").GetBoolean().Should().BeTrue("the sandbox can bill it");
        usage.GetProperty("month").GetString().Should().Be(now.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task The_overview_is_the_same_charge_the_reports_settle_so_the_page_and_the_invoice_agree()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await ActivateAsync(owner.OrganizationId, trialEndsAt: Future.AddDays(-10));
        var calls = new[] { Priced(owner, Future.AddMinutes(-3), 0.0000025m), Priced(owner, Future.AddMinutes(-2), 0.00000125m), Priced(owner, Future.AddMinutes(-1), 3.12m) };
        await SeedAsync(owner.OrganizationId, calls);
        await ReportAsync(owner.OrganizationId, SandboxOptions(), Future);
        var reported = (await ChargesAsync(owner.OrganizationId)).Sum(c => c.Micros);

        await using var db = await VerificationSupport.TenantDbAsync(fixture, owner.OrganizationId);
        var month = await AiUsageBillingQueries.ThisMonthAsync(db, Future, 0.20m, Ct);

        month.ChargeMicros.Should().Be(reported, "the page sums the same rounded charges the meter received");
        month.ChargeMicros.Should().Be(3 + 2 + 3_744_000);
    }

    [Fact]
    public async Task The_overview_carries_the_usage_for_the_owner_and_nothing_for_a_signed_out_request()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        (await owner.BillingAsync()).At("aiUsage").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Object);

        using var browser = new HttpClient { BaseAddress = fixture.GetEndpoint("api") };
        using var response = await browser.GetAsync(BillingTestSupport.Overview, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- Support ----

    private static BillingOptions SandboxOptions() => new() { Sandbox = new BillingSandboxOptions { Enabled = true } };

    private static BillingOptions StripeOptions() => new()
    {
        SecretKey = "sk_test_fictional_key",
        WebhookSecret = "whsec_fictional_secret",
        Ai = new BillingAiOptions { StripePriceId = "price_meter_fictional" },
    };

    private static AiUsageCall Priced(Tenant owner, DateTimeOffset at, decimal cost, AiKeySource key = AiKeySource.BuiltIn) => new()
    {
        OrganizationId = owner.OrganizationId,
        OccurredAt = at,
        CheckName = AiUsageChecks.ConsistencyComparison,
        Provider = "Fake",
        Model = FakeConsistencyChatClient.ModelId,
        KeySource = key,
        ProviderRequestId = $"seed-{Guid.NewGuid():N}",
        InputTokens = 1000,
        OutputTokens = 100,
        Cost = cost,
        CostBasis = AiCostBasis.Priced,
        Outcome = AiCallOutcome.Ok,
    };

    private static AiUsageCall Unpriced(Tenant owner, DateTimeOffset at) => new()
    {
        OrganizationId = owner.OrganizationId,
        OccurredAt = at,
        CheckName = AiUsageChecks.ConsistencyComparison,
        Provider = "Fake",
        Model = FakeConsistencyChatClient.ModelId,
        KeySource = AiKeySource.BuiltIn,
        ProviderRequestId = $"seed-{Guid.NewGuid():N}",
        InputTokens = 1000,
        OutputTokens = 100,
        CostBasis = AiCostBasis.Unknown,
        Outcome = AiCallOutcome.Ok,
    };

    private async Task SeedAsync(Guid organizationId, params AiUsageCall[] calls)
    {
        await using var db = await VerificationSupport.TenantDbAsync(fixture, organizationId);
        db.AiUsageCalls.AddRange(calls);
        await db.SaveChangesAsync(Ct);
    }

    // The organization's account as a paying one: the trial ending at <trialEndsAt> (a fixed instant, since the clock is), and the
    // subscription the sandbox would have made, with a customer for Stripe to name.
    private async Task ActivateAsync(Guid organizationId, DateTimeOffset trialEndsAt, SubscriptionStatus status = SubscriptionStatus.Active, string? customer = "cus_fictional")
    {
        await using var db = await VerificationSupport.TenantDbAsync(fixture, organizationId);
        var account = await db.BillingAccounts.SingleOrDefaultAsync(Ct);
        if (account is null)
        {
            account = BillingAccount.StartTrial(organizationId, trialEndsAt.AddDays(-14), 14);
            db.BillingAccounts.Add(account);
        }
        else
        {
            db.Entry(account).Property(a => a.TrialEndsAt).CurrentValue = trialEndsAt;
        }

        account.Apply(Future.AddYears(1), customer, "sub_fictional", "starter", status, trialEndsAt.AddMonths(1), false, Future);
        await db.SaveChangesAsync(Ct);
    }

    private async Task<IReadOnlyList<AiUsageCharge>> ChargesAsync(Guid organizationId)
    {
        await using var db = await VerificationSupport.TenantDbAsync(fixture, organizationId);
        return await db.AiUsageCharges.AsNoTracking().OrderBy(c => c.SettledAt).ThenBy(c => c.Id).ToListAsync(Ct);
    }

    // Runs the handler as the worker would, with its own save, at <now>. <stripe> stands in for Stripe's servers.
    private async Task<OutgoingMessages> ReportAsync(
        Guid organizationId, BillingOptions options, DateTimeOffset now, string slot = "slot", Guid? after = null, StubStripe? stripe = null)
    {
        var settings = Microsoft.Extensions.Options.Options.Create(options);
        var catalogue = new PlanCatalogue(settings);
        using var realStripe = new StripeBillingProvider(settings, catalogue, stripe ?? new StubStripe());
        var sandbox = new SandboxBillingProvider(new EphemeralDataProtectionProvider(), TimeProvider.System, settings);
        var access = new BillingAccess(settings, realStripe, sandbox);
        await using var db = await VerificationSupport.TenantDbAsync(fixture, organizationId);
        var outgoing = await ReportOrganizationAiUsageHandler.Handle(
            new ReportOrganizationAiUsage(organizationId, slot, after), db, access, settings, new FixedClock(now),
            NullLogger<ReportOrganizationAiUsage>.Instance, Ct);
        await db.SaveChangesAsync(Ct);
        return outgoing;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // Stripe's servers: takes the first <acceptFirst> meter events and refuses the next with a 500.
    private sealed class StubStripe(int acceptFirst = int.MaxValue) : HttpMessageHandler
    {
        private int accepted;

        private int limit = acceptFirst;

        public List<Dictionary<string, string>> Forms { get; } = [];

        public IEnumerable<string> Identifiers => Forms.Select(f => f["identifier"]);

        public void Reset(int acceptFirst)
        {
            Forms.Clear();
            accepted = 0;
            limit = acceptFirst;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Forms.Add(Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(body).ToDictionary(p => p.Key, p => p.Value.ToString()));
            if (accepted++ < limit)
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
            }

            return new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("""{"error":{"type":"api_error"}}""") };
        }
    }
}
