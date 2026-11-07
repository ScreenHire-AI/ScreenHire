using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Billing;
using ScreenHire.Domain.Billing;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.IntegrationTests.Billing;

/// <summary>
/// What the provider reports (ADR-0029). The test stack runs the sandbox, so Stripe's webhook is not mapped; the signed
/// delivery itself is covered by the unit tests of <c>StripeBillingProvider.ReadWebhook</c>. What a delivery does once read
/// (applied once per event id, never undone by an older event, only for the tenant it names) is <see cref="BillingService"/>,
/// run here against the stack's database as the application role, so row-level security and the unique index apply.
/// </summary>
public sealed class BillingWebhookTests(AppHostFixture fixture)
{
    private static readonly DateTimeOffset At = new(2030, 1, 1, 9, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Stripes_webhook_is_a_404_when_the_stack_is_not_in_stripe_mode_even_for_a_signed_looking_post()
    {
        using var anonymous = fixture.CreateHttpClient("api");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/webhooks/stripe") { Content = new StringContent("""{"id":"evt_fictional","type":"invoice.paid"}""") };
        request.Headers.Add("Stripe-Signature", "t=1772442000,v1=deadbeef");

        using var response = await anonymous.SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_event_applies_once_a_redelivery_changes_nothing_and_an_older_event_cannot_undo_a_newer_one()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var organization = owner.OrganizationId;

        (await Apply(organization, new Event("evt_active", At, organization, SubscriptionStatus.Active, "starter"))).Should().BeTrue();
        (await owner.BillingAsync()).Text("state").Should().Be("Active");
        (await owner.BillingAsync()).Text("currentPlan.key").Should().Be("starter");

        (await Apply(organization, new Event("evt_active", At, organization, SubscriptionStatus.Active, "starter")))
            .Should().BeFalse("the same event id was applied before");
        (await Apply(organization, new Event("evt_stale_end", At.AddHours(-1), organization, SubscriptionStatus.Ended, null)))
            .Should().BeFalse("it is older than what the account already knows");
        var afterStale = await owner.BillingAsync();
        afterStale.Text("state").Should().Be("Active", "a late cancel cannot undo the newer payment");

        (await Apply(organization, new Event("evt_end", At.AddHours(1), organization, SubscriptionStatus.Ended, null))).Should().BeTrue();
        (await owner.BillingAsync()).Text("state").Should().Be("Ended");
        (await Apply(organization, new Event("evt_late_active", At.AddMinutes(30), organization, SubscriptionStatus.Active, "growth")))
            .Should().BeFalse("it is older than the cancel");
        (await owner.BillingAsync()).Text("state").Should().Be("Ended");
        (await Counts(organization)).Should().Be(4, "evt_active, evt_stale_end, evt_end and evt_late_active are each recorded once");
    }

    [Fact]
    public async Task An_event_that_names_another_organization_is_not_applied_to_this_tenant()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();

        var applied = await Apply(owner.OrganizationId, new Event("evt_foreign", At, other.OrganizationId, SubscriptionStatus.Active, "unlimited"));

        applied.Should().BeFalse();
        (await owner.BillingAsync()).Text("state").Should().Be("Trialing");
        (await other.BillingAsync()).Text("state").Should().Be("Trialing");
    }

    [Fact]
    public async Task An_event_id_applied_to_one_organization_does_not_block_another_organizations_event()
    {
        using var first = await fixture.SignedInWithOrganizationAsync();
        using var second = await fixture.SignedInWithOrganizationAsync();

        (await Apply(first.OrganizationId, new Event("evt_shared_id", At, first.OrganizationId, SubscriptionStatus.Active, "starter"))).Should().BeTrue();
        (await Apply(second.OrganizationId, new Event("evt_shared_id", At, second.OrganizationId, SubscriptionStatus.Active, "growth"))).Should().BeTrue();

        (await first.BillingAsync()).Text("currentPlan.key").Should().Be("starter");
        (await second.BillingAsync()).Text("currentPlan.key").Should().Be("growth");
    }

    [Fact]
    public async Task An_end_for_a_subscription_other_than_the_current_one_is_recorded_but_leaves_the_account_active()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var organization = owner.OrganizationId;
        (await Apply(organization, new Event("evt_active", At, organization, SubscriptionStatus.Active, "starter", "sub_current"))).Should().BeTrue();

        (await Apply(organization, new Event("evt_old_end", At.AddHours(1), organization, SubscriptionStatus.Ended, null, "sub_replaced")))
            .Should().BeFalse("the subscription that pays is not the one that ended");

        var billing = await owner.BillingAsync();
        billing.Text("state").Should().Be("Active");
        billing.Text("currentPlan.key").Should().Be("starter");
        (await Counts(organization)).Should().Be(2, "the ignored end is recorded, so a redelivery is a no-op");
        (await Apply(organization, new Event("evt_old_end", At.AddHours(1), organization, SubscriptionStatus.Ended, null, "sub_replaced")))
            .Should().BeFalse("the same event id was recorded before");
        (await Counts(organization)).Should().Be(2);
        (await owner.BillingAsync()).Text("state").Should().Be("Active");
    }

    [Fact]
    public async Task An_end_for_the_current_subscription_ends_the_account()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var organization = owner.OrganizationId;
        (await Apply(organization, new Event("evt_active", At, organization, SubscriptionStatus.Active, "starter", "sub_current"))).Should().BeTrue();

        (await Apply(organization, new Event("evt_end", At.AddHours(1), organization, SubscriptionStatus.Ended, null, "sub_current"))).Should().BeTrue();

        (await owner.BillingAsync()).Text("state").Should().Be("Ended");
    }

    [Fact]
    public async Task An_end_that_names_no_subscription_ends_the_account()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var organization = owner.OrganizationId;
        (await Apply(organization, new Event("evt_active", At, organization, SubscriptionStatus.Active, "starter", "sub_current"))).Should().BeTrue();

        (await Apply(organization, new Event("evt_end", At.AddHours(1), organization, SubscriptionStatus.Ended, null, null))).Should().BeTrue();

        (await owner.BillingAsync()).Text("state").Should().Be("Ended");
    }

    private sealed record Event(
        string Id, DateTimeOffset CreatedAt, Guid Organization, SubscriptionStatus Status, string? Plan, string? Subscription = "sub_fictional");

    /// <summary>Applies the event as the tenant <paramref name="tenantOrganization"/> and saves, as the webhook's route does.</summary>
    private async Task<bool> Apply(Guid tenantOrganization, Event evt)
    {
        var tenant = new TenantContext();
        tenant.SetPublicOrganization(tenantOrganization);
        await using var db = await fixture.CreateDbContextAsync(tenant, Ct);
        var options = Options.Create(new BillingOptions { Sandbox = new BillingSandboxOptions { Enabled = true } });
        var time = new FixedTime(At);
        var access = new BillingAccess(options, new StripeBillingProvider(options, new PlanCatalogue(options)), new SandboxBillingProvider(new EphemeralDataProtectionProvider(), time));
        // The open-job counter is only used to read the page and to check a publish, neither of which this calls.
        var service = new BillingService(db, access, new PlanCatalogue(options), null!, options, time);
        var providerEvent = new BillingProviderEvent(evt.Id, evt.CreatedAt, evt.Organization, "cus_fictional", evt.Subscription, evt.Plan, evt.Status, null, false);

        var changed = await service.ApplyAsync(providerEvent, tenantOrganization, Ct);
        await db.SaveChangesAsync(Ct);
        return changed;
    }

    private async Task<int> Counts(Guid organization)
    {
        var tenant = new TenantContext();
        tenant.SetOrganization(organization);
        await using var db = await fixture.CreateDbContextAsync(tenant, Ct);
        return await db.BillingEvents.CountAsync(Ct);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
