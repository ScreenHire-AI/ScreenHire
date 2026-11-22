using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Billing;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.IntegrationTests.Billing;

/// <summary>
/// <c>/v1/organization/billing</c> against the local sandbox provider (docs/architecture/billing.md, ADR-0029): a new
/// organization trials for 14 days on the Growth plan's limit, the Owner alone reads and changes billing, the sandbox's
/// checkout page activates a plan and its portal page cancels it, and one organization never sees another's account. The
/// test stack sells the plans <c>packages/plans/plans.json</c> ships, which the tests read through <see cref="ShippedPlans"/>.
/// </summary>
public sealed class BillingTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_organization_is_on_a_fourteen_day_trial_of_the_growth_plan_and_the_page_lists_the_plans()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();

        var billing = await owner.BillingAsync();

        billing.At("available").GetBoolean().Should().BeTrue();
        billing.At("sandbox").GetBoolean().Should().BeTrue();
        billing.Text("state").Should().Be("Trialing");
        billing.Text("currentPlan.key").Should().Be("growth");
        billing.At("activeJobLimit").GetInt32().Should().Be(ShippedPlans.Growth.RequiredLimit);
        billing.At("openJobs").GetInt32().Should().Be(0);
        billing.At("trialEndsAt").GetDateTimeOffset().Should()
            .BeCloseTo(DateTimeOffset.UtcNow.AddDays(14), TimeSpan.FromHours(1));
        billing.At("nextPaymentAt").ValueKind.Should().Be(JsonValueKind.Null);
        billing.At("nextPaymentAmount").ValueKind.Should().Be(JsonValueKind.Null);
        billing.At("cancelsAtPeriodEnd").GetBoolean().Should().BeFalse();
        billing.At("canManage").GetBoolean().Should().BeFalse("there is nothing to manage before a subscription");
        billing.At("taxAtCheckout").GetBoolean().Should().BeFalse("tax is off until the owner has registered in Stripe Tax");

        var plans = billing.At("plans").EnumerateArray().ToDictionary(p => p.GetProperty("key").GetString()!);
        plans.Keys.Should().Equal("starter", "growth", "unlimited");
        plans["starter"].GetProperty("monthlyPrice").GetDecimal().Should().Be(ShippedPlans.Starter.MonthlyPrice);
        plans["starter"].GetProperty("activeJobs").GetInt32().Should().Be(ShippedPlans.Starter.RequiredLimit);
        plans["growth"].GetProperty("monthlyPrice").GetDecimal().Should().Be(ShippedPlans.Growth.MonthlyPrice);
        plans["growth"].GetProperty("activeJobs").GetInt32().Should().Be(ShippedPlans.Growth.RequiredLimit);
        plans["unlimited"].GetProperty("monthlyPrice").GetDecimal().Should().Be(ShippedPlans.Unlimited.MonthlyPrice);
        plans["unlimited"].GetProperty("unlimited").GetBoolean().Should().BeTrue();
        plans["unlimited"].GetProperty("activeJobs").ValueKind.Should().Be(JsonValueKind.Null);
        plans.Values.Should().OnlyContain(p => p.GetProperty("canSubscribe").GetBoolean(), "the sandbox can start any plan");
    }

    [Fact]
    public async Task Onboarding_creates_the_billing_account_with_a_trial_ending_in_fourteen_days()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var tenant = new TenantContext();
        tenant.SetOrganization(owner.OrganizationId);
        await using var db = await fixture.CreateDbContextAsync(tenant, Ct);

        var accounts = await db.BillingAccounts.ToListAsync(Ct);

        var account = accounts.Should().ContainSingle().Subject;
        account.OrganizationId.Should().Be(owner.OrganizationId);
        account.Status.Should().Be(SubscriptionStatus.None);
        account.PlanKey.Should().BeNull();
        account.TrialEndsAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(14), TimeSpan.FromHours(1));
    }

    [Fact]
    public async Task Only_the_owner_reads_or_changes_billing_and_admins_and_recruiters_get_403()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var admin = await fixture.InviteAndJoinAsync(owner, "Admin");
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer");

        foreach (var member in new[] { admin, recruiter, viewer })
        {
            (await member.GetAsync(BillingTestSupport.Overview)).Status.Should().Be(HttpStatusCode.Forbidden);
            (await member.PostAsync($"{BillingTestSupport.Overview}/checkout", new JsonObject { ["plan"] = "starter" })).Status.Should().Be(HttpStatusCode.Forbidden);
            (await member.PostAsync($"{BillingTestSupport.Overview}/portal")).Status.Should().Be(HttpStatusCode.Forbidden);
        }

        (await owner.BillingAsync()).Text("state").Should().Be("Trialing", "a refused checkout changes nothing");
    }

    [Fact]
    public async Task A_signed_out_request_is_unauthorized()
    {
        using var anonymous = fixture.CreateHttpClient("api");

        using var response = await anonymous.GetAsync(BillingTestSupport.Overview, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Checkout_through_the_sandbox_activates_the_plan_and_the_page_shows_the_next_payment()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();

        var checkout = (await owner.PostAsync($"{BillingTestSupport.Overview}/checkout", new JsonObject { ["plan"] = "starter" })).Expect(HttpStatusCode.OK);

        checkout.Text("url").Should().StartWith("/v1/billing/sandbox/checkout?state=");
        (await owner.BillingAsync()).Text("state").Should().Be("Trialing", "nothing is paid until the page is followed");
        var (status, location) = await fixture.FollowAsync(checkout.Text("url"));
        status.Should().Be(HttpStatusCode.Redirect);
        location!.AbsolutePath.Should().Be("/settings/billing");
        location.Query.Should().Contain("checkout=success").And.NotContain("tax", "the shared stack has tax off, so the checkout asks for none");

        var billing = await owner.BillingAsync();
        billing.Text("state").Should().Be("Active");
        billing.Text("currentPlan.key").Should().Be("starter");
        billing.At("activeJobLimit").GetInt32().Should().Be(ShippedPlans.Starter.RequiredLimit);
        billing.At("trialEndsAt").ValueKind.Should().Be(JsonValueKind.Null);
        billing.At("nextPaymentAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow.AddMonths(1), TimeSpan.FromHours(1));
        billing.At("nextPaymentAmount").GetDecimal().Should().Be(ShippedPlans.Starter.MonthlyPrice);
        billing.At("cancelsAtPeriodEnd").GetBoolean().Should().BeFalse();
        billing.At("canManage").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task A_second_checkout_while_subscribed_is_a_conflict_and_changes_nothing()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await fixture.SubscribeAsync(owner, "starter");

        var second = (await owner.PostAsync($"{BillingTestSupport.Overview}/checkout", new JsonObject { ["plan"] = "growth" })).Expect(HttpStatusCode.Conflict);

        second.Text("detail").Should().Contain("already has a subscription");
        (await owner.BillingAsync()).Text("currentPlan.key").Should().Be("starter");
    }

    [Fact]
    public async Task The_portal_cancels_the_subscription_at_once_and_the_organization_is_ended()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await fixture.SubscribeAsync(owner, "growth");

        await fixture.CancelSubscriptionAsync(owner);

        var billing = await owner.BillingAsync();
        billing.Text("state").Should().Be("Ended");
        billing.At("nextPaymentAt").ValueKind.Should().Be(JsonValueKind.Null);
        billing.At("nextPaymentAmount").ValueKind.Should().Be(JsonValueKind.Null);
        billing.At("currentPlan").ValueKind.Should().Be(JsonValueKind.Null, "an ended organization has no plan in force");
        billing.At("activeJobLimit").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task An_ended_organization_can_subscribe_again()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await fixture.SubscribeAsync(owner, "starter");
        await fixture.CancelSubscriptionAsync(owner);

        await fixture.SubscribeAsync(owner, "unlimited");

        var billing = await owner.BillingAsync();
        billing.Text("state").Should().Be("Active");
        billing.Text("currentPlan.key").Should().Be("unlimited");
    }

    [Fact]
    public async Task The_portal_before_any_subscription_is_a_conflict()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();

        var portal = (await owner.PostAsync($"{BillingTestSupport.Overview}/portal")).Expect(HttpStatusCode.Conflict);

        portal.Text("detail").Should().Contain("no subscription");
    }

    [Theory]
    [InlineData("platinum")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_unknown_plan_is_a_validation_problem_on_plan(string plan)
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();

        var refused = (await owner.PostAsync($"{BillingTestSupport.Overview}/checkout", new JsonObject { ["plan"] = plan })).Expect(HttpStatusCode.BadRequest);

        refused.ProblemKeys().Should().Equal("plan");
        (await owner.BillingAsync()).Text("state").Should().Be("Trialing");
    }

    [Fact]
    public async Task A_sandbox_link_with_a_missing_forged_or_wrong_kind_of_state_is_refused_and_activates_nothing()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await fixture.SubscribeAsync(owner, "starter");
        var portal = (await owner.PostAsync($"{BillingTestSupport.Overview}/portal")).Expect(HttpStatusCode.OK).Text("url");
        var portalState = portal[(portal.IndexOf("state=", StringComparison.Ordinal) + "state=".Length)..];

        foreach (var link in new[]
                 {
                     "/v1/billing/sandbox/checkout",
                     "/v1/billing/sandbox/checkout?state=forged",
                     "/v1/billing/sandbox/portal?state=forged",
                     "/v1/billing/sandbox/portal?state=" + portalState[..^2],
                     "/v1/billing/sandbox/checkout?state=" + portalState, // a portal state names no plan to buy
                 })
        {
            (await fixture.FollowAsync(link)).Status.Should().Be(HttpStatusCode.BadRequest, link);
        }

        var billing = await owner.BillingAsync();
        billing.Text("state").Should().Be("Active");
        billing.Text("currentPlan.key").Should().Be("starter");
    }

    [Fact]
    public async Task One_organization_never_sees_anothers_billing()
    {
        using var first = await fixture.SignedInWithOrganizationAsync();
        using var second = await fixture.SignedInWithOrganizationAsync();

        await fixture.SubscribeAsync(first, "unlimited");

        var other = await second.BillingAsync();
        other.Text("state").Should().Be("Trialing");
        other.Text("currentPlan.key").Should().Be("growth");
        var tenant = new TenantContext();
        tenant.SetOrganization(second.OrganizationId);
        await using var db = await fixture.CreateDbContextAsync(tenant, Ct);
        var visible = await db.BillingAccounts.ToListAsync(Ct);
        visible.Should().ContainSingle().Which.OrganizationId.Should().Be(second.OrganizationId);
        (await first.BillingAsync()).Text("currentPlan.key").Should().Be("unlimited");
    }

    [Fact]
    public async Task Invoices_and_the_card_come_from_the_provider_once_there_is_a_customer_and_only_the_owner_reads_them()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var admin = await fixture.InviteAndJoinAsync(owner, "Admin");

        (await owner.GetAsync($"{BillingTestSupport.Overview}/invoices")).Expect(HttpStatusCode.OK).Count("invoices").Should().Be(0, "the trial has no customer and so no invoices");
        (await owner.GetAsync($"{BillingTestSupport.Overview}/payment-method")).Expect(HttpStatusCode.OK).At("method").ValueKind.Should().Be(JsonValueKind.Null);

        await fixture.SubscribeAsync(owner, "starter");

        var invoices = (await owner.GetAsync($"{BillingTestSupport.Overview}/invoices")).Expect(HttpStatusCode.OK);
        invoices.Count("invoices").Should().Be(3, "the sandbox lists three sample invoices");
        var dates = invoices.At("invoices").EnumerateArray().Select(i => i.GetProperty("date").GetDateTimeOffset()).ToList();
        dates.Should().BeInDescendingOrder("newest first");
        invoices.At("invoices.0.amount").GetDecimal().Should().Be(ShippedPlans.Starter.MonthlyPrice);
        invoices.Text("invoices.0.currency").Should().Be("USD");
        invoices.Text("invoices.0.status").Should().Be("Paid");
        var pdf = invoices.Text("invoices.0.pdfUrl");
        pdf.Should().StartWith("/v1/billing/sandbox/invoices/in_sandbox_");

        using (var file = await owner.Session.Client.GetAsync(pdf, Ct))
        {
            file.StatusCode.Should().Be(HttpStatusCode.OK);
            file.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
            (await file.Content.ReadAsStringAsync(Ct)).Should().StartWith("%PDF-");
        }

        var card = (await owner.GetAsync($"{BillingTestSupport.Overview}/payment-method")).Expect(HttpStatusCode.OK);
        card.Text("method.type").Should().Be("card");
        card.Text("method.brand").Should().Be("visa");
        card.Text("method.last4").Should().Be("4242");

        (await admin.GetAsync($"{BillingTestSupport.Overview}/invoices")).Status.Should().Be(HttpStatusCode.Forbidden);
        (await admin.GetAsync($"{BillingTestSupport.Overview}/payment-method")).Status.Should().Be(HttpStatusCode.Forbidden);
        (await admin.Session.Client.GetAsync(pdf, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "the invoice file is for who may see the invoices");
        using var anonymous = fixture.CreateHttpClient("api");
        (await anonymous.GetAsync($"{BillingTestSupport.Overview}/invoices", Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_portal_opened_to_change_the_card_leaves_the_plan_and_opened_to_cancel_ends_it()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await fixture.SubscribeAsync(owner, "growth");

        var change = (await owner.PostAsync($"{BillingTestSupport.Overview}/portal?intent=ChangeCard")).Expect(HttpStatusCode.OK);
        (await fixture.FollowAsync(change.Text("url"))).Status.Should().Be(HttpStatusCode.Redirect);
        (await owner.BillingAsync()).Text("state").Should().Be("Active", "changing the card is not cancelling");

        var cancel = (await owner.PostAsync($"{BillingTestSupport.Overview}/portal?intent=Cancel")).Expect(HttpStatusCode.OK);
        (await fixture.FollowAsync(cancel.Text("url"))).Status.Should().Be(HttpStatusCode.Redirect);
        (await owner.BillingAsync()).Text("state").Should().Be("Ended");
    }

    [Fact]
    public async Task A_paying_month_adds_the_credits_bought_to_the_plan_and_estimates_the_next_invoice_as_the_plan_alone()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await fixture.SubscribeAsync(owner, "starter");
        await fixture.BuyCreditsAsync(owner, 25m);
        ApiResult? billing = null;
        await Messaging.WolverineStore.WaitUntilAsync(
            async () =>
            {
                billing = await owner.BillingAsync();
                return billing.At("thisMonth").GetProperty("creditsBought").GetDecimal() == 25m;
            },
            "the worker to grant the credits that were bought",
            Ct);

        var month = billing!.At("thisMonth");
        month.GetProperty("planName").GetString().Should().Be("Starter");
        month.GetProperty("planAmount").GetDecimal().Should().Be(ShippedPlans.Starter.MonthlyPrice);
        month.GetProperty("total").GetDecimal().Should().Be(ShippedPlans.Starter.MonthlyPrice + 25m);
        month.GetProperty("nextInvoiceAt").GetDateTimeOffset().Should().Be(billing.At("nextPaymentAt").GetDateTimeOffset());
        month.GetProperty("nextInvoiceAmount").GetDecimal().Should().Be(ShippedPlans.Starter.MonthlyPrice, "credits are paid for when bought and are on no invoice");
    }
}
