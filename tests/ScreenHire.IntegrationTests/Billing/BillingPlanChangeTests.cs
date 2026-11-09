using System.Text.Json;
using System.Text.Json.Nodes;
using ScreenHire.Domain.Billing;

namespace ScreenHire.IntegrationTests.Billing;

/// <summary>
/// <c>/v1/organization/billing/plan/preview</c> and <c>/plan</c> against the local sandbox provider
/// (docs/architecture/billing.md): the Owner of a paying organization sees what a switch costs before making it, confirms it
/// with the date the price was worked out at, and a trial, an ended subscription or another role gets nothing. The sandbox
/// prorates by the day, so the amounts are checked against the shipped plans' prices as bounds, not as exact cents.
/// </summary>
public sealed class BillingPlanChangeTests(AppHostFixture fixture)
{
    private const string Preview = BillingTestSupport.Overview + "/plan/preview";
    private const string Change = BillingTestSupport.Overview + "/plan";

    private static JsonObject Plan(string key) => new() { ["plan"] = key };

    [Fact]
    public async Task An_owner_sees_what_an_upgrade_costs_now_and_then_each_month_and_nothing_changes()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await fixture.SubscribeAsync(owner, "starter");

        var preview = (await owner.PostAsync(Preview, Plan("growth"))).Expect(HttpStatusCode.OK);

        preview.Text("plan").Should().Be("growth");
        preview.Text("currency").Should().Be("USD");
        var difference = ShippedPlans.Growth.MonthlyPrice!.Value - ShippedPlans.Starter.MonthlyPrice!.Value;
        preview.At("amountDueNow").GetDecimal().Should().BeInRange(0.01m, difference, "the rest of the month is a part of the price difference");
        preview.At("nextPaymentAmount").GetDecimal().Should().Be(ShippedPlans.Growth.MonthlyPrice);
        preview.At("nextPaymentAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow.AddMonths(1), TimeSpan.FromHours(1));
        preview.At("prorationDate").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));

        var billing = await owner.BillingAsync();
        billing.Text("currentPlan.key").Should().Be("starter", "a preview changes nothing");
        billing.At("nextPaymentAmount").GetDecimal().Should().Be(ShippedPlans.Starter.MonthlyPrice);
    }

    [Fact]
    public async Task A_downgrade_owes_nothing_now_and_the_lower_price_is_next()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await fixture.SubscribeAsync(owner, "growth");

        var preview = (await owner.PostAsync(Preview, Plan("starter"))).Expect(HttpStatusCode.OK);

        preview.At("amountDueNow").GetDecimal().Should().Be(0m);
        preview.At("nextPaymentAmount").GetDecimal().Should().Be(ShippedPlans.Starter.MonthlyPrice);
    }

    [Fact]
    public async Task Confirming_with_the_previews_date_switches_the_plan_and_the_page_shows_it()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await fixture.SubscribeAsync(owner, "starter");
        var preview = (await owner.PostAsync(Preview, Plan("growth"))).Expect(HttpStatusCode.OK);

        (await owner.PostAsync(Change, new JsonObject { ["plan"] = "growth", ["prorationDate"] = preview.Text("prorationDate") })).Expect(HttpStatusCode.NoContent);

        var billing = await owner.BillingAsync();
        billing.Text("state").Should().Be("Active");
        billing.Text("currentPlan.key").Should().Be("growth");
        billing.At("activeJobLimit").GetInt32().Should().Be(ShippedPlans.Growth.RequiredLimit);
        billing.At("nextPaymentAmount").GetDecimal().Should().Be(ShippedPlans.Growth.MonthlyPrice);
    }

    [Fact]
    public async Task A_confirmation_priced_long_ago_or_in_the_future_is_refused_and_changes_nothing()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await fixture.SubscribeAsync(owner, "starter");

        foreach (var date in new[] { DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow.AddHours(2) })
        {
            var refused = (await owner.PostAsync(Change, new JsonObject { ["plan"] = "growth", ["prorationDate"] = date.ToString("O") })).Expect(HttpStatusCode.BadRequest);
            refused.ProblemKeys().Should().Equal("prorationDate");
        }

        (await owner.BillingAsync()).Text("currentPlan.key").Should().Be("starter");
    }

    [Fact]
    public async Task A_trial_has_no_subscription_to_change_so_both_routes_are_a_conflict()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();

        var preview = (await owner.PostAsync(Preview, Plan("starter"))).Expect(HttpStatusCode.Conflict);
        (await owner.PostAsync(Change, new JsonObject { ["plan"] = "starter", ["prorationDate"] = DateTimeOffset.UtcNow.ToString("O") })).Expect(HttpStatusCode.Conflict);

        preview.Text("detail").Should().Contain("no subscription to change").And.Contain("Choose a plan");
        (await owner.BillingAsync()).Text("state").Should().Be("Trialing");
    }

    [Fact]
    public async Task An_ended_subscription_is_a_conflict_too()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await fixture.SubscribeAsync(owner, "growth");
        await fixture.CancelSubscriptionAsync(owner);

        (await owner.PostAsync(Preview, Plan("starter"))).Expect(HttpStatusCode.Conflict).Text("detail").Should().Contain("no subscription to change");
    }

    [Theory]
    [InlineData("platinum")]
    [InlineData("")]
    [InlineData("starter")]
    public async Task An_unknown_plan_or_the_plan_the_organization_is_on_is_a_validation_problem_on_plan(string plan)
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await fixture.SubscribeAsync(owner, "starter");

        var refused = (await owner.PostAsync(Preview, Plan(plan))).Expect(HttpStatusCode.BadRequest);

        refused.ProblemKeys().Should().Equal("plan");
    }

    [Fact]
    public async Task Only_the_owner_may_preview_or_change_a_plan()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var admin = await fixture.InviteAndJoinAsync(owner, "Admin");
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        await fixture.SubscribeAsync(owner, "starter");
        var body = new JsonObject { ["plan"] = "growth", ["prorationDate"] = DateTimeOffset.UtcNow.ToString("O") };

        foreach (var member in new[] { admin, recruiter })
        {
            (await member.PostAsync(Preview, Plan("growth"))).Status.Should().Be(HttpStatusCode.Forbidden);
            (await member.PostAsync(Change, body)).Status.Should().Be(HttpStatusCode.Forbidden);
        }

        (await owner.BillingAsync()).Text("currentPlan.key").Should().Be("starter");
    }
}
