using ScreenHire.Domain.Billing;

namespace ScreenHire.UnitTests.Billing;

/// <summary>
/// What a billing account allows (ADR-0029): a trial runs to a fixed date, a subscription replaces it, a payment that
/// fails keeps publishing, an ended organization cannot open drafts, and a plan's limit counts the jobs open across the
/// whole organization.
/// </summary>
public sealed class EntitlementTests
{
    private static readonly DateTimeOffset Start = new(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Organization = Guid.CreateVersion7();

    private static BillingAccount Trial() => BillingAccount.StartTrial(Organization, Start, 14);

    private static BillingAccount WithStatus(SubscriptionStatus status, string? plan = "starter")
    {
        var account = Trial();
        account.Apply(Start.AddDays(1), "cus_fictional", "sub_fictional", plan, status, Start.AddDays(31), false, Start.AddDays(1));
        return account;
    }

    [Fact]
    public void A_new_account_trials_until_the_configured_number_of_days_from_sign_up()
    {
        var account = Trial();

        account.TrialEndsAt.Should().Be(Start.AddDays(14));
        account.Status.Should().Be(SubscriptionStatus.None);
        account.OrganizationId.Should().Be(Organization);
        account.PlanKey.Should().BeNull();
    }

    [Fact]
    public void A_trial_is_trialing_until_its_last_moment_and_ended_from_then_on()
    {
        var account = Trial();

        Entitlement.StateOf(account, Start).Should().Be(BillingState.Trialing);
        Entitlement.StateOf(account, Start.AddDays(14).AddSeconds(-1)).Should().Be(BillingState.Trialing);
        Entitlement.StateOf(account, Start.AddDays(14)).Should().Be(BillingState.Ended);
        Entitlement.StateOf(account, Start.AddDays(60)).Should().Be(BillingState.Ended, "a trial over with no subscription is ended");
    }

    [Theory]
    [InlineData(SubscriptionStatus.Active, BillingState.Active)]
    [InlineData(SubscriptionStatus.PastDue, BillingState.PastDue)]
    [InlineData(SubscriptionStatus.Ended, BillingState.Ended)]
    public void A_subscription_decides_the_state_whatever_the_trial_says(SubscriptionStatus status, BillingState expected)
    {
        var account = WithStatus(status);

        Entitlement.StateOf(account, Start.AddDays(2)).Should().Be(expected, "inside the trial window");
        Entitlement.StateOf(account, Start.AddDays(90)).Should().Be(expected, "long after the trial");
    }

    [Fact]
    public void The_plan_in_force_is_the_trials_while_trialing_and_the_subscriptions_afterwards()
    {
        var now = Start.AddDays(2);

        Entitlement.EffectivePlanKey(Trial(), now, "growth").Should().Be("growth");
        Entitlement.EffectivePlanKey(WithStatus(SubscriptionStatus.Active, "starter"), now, "growth").Should().Be("starter");
        Entitlement.EffectivePlanKey(WithStatus(SubscriptionStatus.PastDue, "unlimited"), now, "growth").Should().Be("unlimited");
    }

    [Fact]
    public void An_ended_account_and_a_subscription_with_no_plan_have_no_plan_in_force()
    {
        var now = Start.AddDays(2);

        Entitlement.EffectivePlanKey(Trial(), Start.AddDays(20), "growth").Should().BeNull("the trial is over");
        Entitlement.EffectivePlanKey(WithStatus(SubscriptionStatus.Ended), now, "growth").Should().BeNull();
        Entitlement.EffectivePlanKey(WithStatus(SubscriptionStatus.Active, plan: null), now, "growth").Should().BeNull();
    }

    [Fact]
    public void A_trial_may_open_drafts_until_the_plans_limit_is_reached()
    {
        var trial = Trial();

        Entitlement.CanPublishDraft(trial, Start, 50, 0).IsAllowed.Should().BeTrue();
        Entitlement.CanPublishDraft(trial, Start, 50, 49).IsAllowed.Should().BeTrue();
        var atLimit = Entitlement.CanPublishDraft(trial, Start, 50, 50);
        atLimit.Kind.Should().Be(PublishDecisionKind.LimitReached);
        atLimit.Limit.Should().Be(50);
        atLimit.OpenJobs.Should().Be(50);
    }

    [Fact]
    public void Over_the_limit_is_refused_too()
    {
        var over = Entitlement.CanPublishDraft(WithStatus(SubscriptionStatus.Active), Start.AddDays(2), 1, 3);

        over.Kind.Should().Be(PublishDecisionKind.LimitReached);
        over.Limit.Should().Be(1);
        over.OpenJobs.Should().Be(3);
    }

    [Fact]
    public void A_plan_with_no_limit_allows_any_number_of_open_jobs()
    {
        Entitlement.CanPublishDraft(WithStatus(SubscriptionStatus.Active, "unlimited"), Start.AddDays(2), null, 10_000).IsAllowed.Should().BeTrue();
        Entitlement.CanPublishDraft(Trial(), Start, null, 10_000).IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void An_active_subscription_is_held_to_its_limit()
    {
        var active = WithStatus(SubscriptionStatus.Active);

        Entitlement.CanPublishDraft(active, Start.AddDays(2), 1, 0).IsAllowed.Should().BeTrue();
        Entitlement.CanPublishDraft(active, Start.AddDays(2), 1, 1).Kind.Should().Be(PublishDecisionKind.LimitReached);
    }

    [Fact]
    public void A_late_payment_keeps_publishing_within_the_limit()
    {
        var pastDue = WithStatus(SubscriptionStatus.PastDue);

        Entitlement.CanPublishDraft(pastDue, Start.AddDays(40), 5, 4).IsAllowed.Should().BeTrue();
        Entitlement.CanPublishDraft(pastDue, Start.AddDays(40), 5, 5).Kind.Should().Be(PublishDecisionKind.LimitReached);
    }

    [Fact]
    public void An_ended_organization_cannot_open_a_draft_whatever_the_limit_or_count()
    {
        var canceled = WithStatus(SubscriptionStatus.Ended);

        Entitlement.CanPublishDraft(canceled, Start.AddDays(2), 50, 0).Kind.Should().Be(PublishDecisionKind.SubscriptionEnded);
        Entitlement.CanPublishDraft(canceled, Start.AddDays(2), null, 0).Kind.Should().Be(PublishDecisionKind.SubscriptionEnded);
        Entitlement.CanPublishDraft(Trial(), Start.AddDays(14), 50, 0).Kind.Should().Be(PublishDecisionKind.SubscriptionEnded, "the trial is over");
    }

    [Fact]
    public void Applying_an_event_sets_what_it_carries_and_keeps_what_it_does_not()
    {
        var account = Trial();
        var periodEnd = Start.AddDays(31);
        account.Apply(Start.AddDays(1), "cus_a", "sub_a", "starter", SubscriptionStatus.Active, periodEnd, false, Start.AddDays(1)).Should().BeTrue();

        account.Apply(Start.AddDays(2), null, null, null, null, null, true, Start.AddDays(2)).Should().BeTrue();

        account.ProviderCustomerId.Should().Be("cus_a");
        account.ProviderSubscriptionId.Should().Be("sub_a");
        account.PlanKey.Should().Be("starter");
        account.Status.Should().Be(SubscriptionStatus.Active);
        account.CurrentPeriodEnd.Should().Be(periodEnd);
        account.CancelsAtPeriodEnd.Should().BeTrue();
        account.LastEventAt.Should().Be(Start.AddDays(2));
        account.UpdatedAt.Should().Be(Start.AddDays(2));
    }

    [Fact]
    public void An_event_older_than_one_already_applied_changes_nothing()
    {
        var account = Trial();
        account.Apply(Start.AddDays(5), "cus_a", "sub_a", "growth", SubscriptionStatus.Ended, null, false, Start.AddDays(5));

        var applied = account.Apply(Start.AddDays(3), "cus_b", "sub_b", "starter", SubscriptionStatus.Active, Start.AddDays(33), true, Start.AddDays(6));

        applied.Should().BeFalse();
        account.Status.Should().Be(SubscriptionStatus.Ended);
        account.PlanKey.Should().Be("growth");
        account.ProviderCustomerId.Should().Be("cus_a");
        account.ProviderSubscriptionId.Should().Be("sub_a");
        account.CancelsAtPeriodEnd.Should().BeFalse();
        account.LastEventAt.Should().Be(Start.AddDays(5));
        account.UpdatedAt.Should().Be(Start.AddDays(5));
    }

    [Fact]
    public void An_event_at_the_same_moment_still_applies()
    {
        var account = Trial();
        account.Apply(Start.AddDays(5), null, null, "starter", SubscriptionStatus.Active, null, null, Start.AddDays(5));

        account.Apply(Start.AddDays(5), null, null, "growth", null, null, null, Start.AddDays(5)).Should().BeTrue();

        account.PlanKey.Should().Be("growth");
    }
}
