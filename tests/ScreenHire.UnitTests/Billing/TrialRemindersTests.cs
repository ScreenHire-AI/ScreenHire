using Microsoft.Extensions.Configuration;
using ScreenHire.Application.Billing;
using ScreenHire.Domain.Billing;

namespace ScreenHire.UnitTests.Billing;

/// <summary>
/// When a trial's Owners are reminded (docs/architecture/billing.md, "Trial reminders"): an email a configurable number of days
/// before the end (7 and 1 by default) and when it ends, the bell for the last 7 days, and nothing once a subscription started or
/// the trial is not the one a reminder was scheduled for.
/// </summary>
public sealed class TrialRemindersTests
{
    private static readonly DateTimeOffset Start = new(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Organization = Guid.CreateVersion7();

    private static BillingAccount Trial() => BillingAccount.StartTrial(Organization, Start, 14);

    [Fact]
    public void Nothing_configured_reminds_seven_days_and_one_day_before()
    {
        TrialReminders.Days(null).Should().Equal(7, 1);
        TrialReminders.Days([]).Should().Equal(7, 1);
    }

    [Fact]
    public void Configured_days_replace_the_defaults_latest_first_and_each_once()
    {
        TrialReminders.Days([1, 3, 3, 10]).Should().Equal(10, 3, 1);
    }

    [Fact]
    public void A_day_of_zero_or_less_is_dropped_because_the_day_the_trial_ends_is_always_reminded()
    {
        TrialReminders.Days([0, -2]).Should().Equal(7, 1); // nothing valid is left, so the default applies
        TrialReminders.Days([0, 5]).Should().Equal(5);
    }

    [Fact]
    public void A_fresh_trial_gets_an_email_at_seven_days_and_one_day_and_at_the_end_and_a_bell_notice_at_seven_days()
    {
        var ends = Trial().TrialEndsAt;

        var steps = TrialReminders.Plan(ends, Start, null);

        steps.Should().BeEquivalentTo(
            [
                new TrialStep(TrialStepKind.Email, 7, ends.AddDays(-7)),
                new TrialStep(TrialStepKind.Email, 1, ends.AddDays(-1)),
                new TrialStep(TrialStepKind.Email, 0, ends),
                new TrialStep(TrialStepKind.Notice, 7, ends.AddDays(-7)),
                new TrialStep(TrialStepKind.EndedNotice, 0, ends),
            ]);
    }

    [Fact]
    public void Configured_days_set_the_emails_while_the_bell_still_starts_seven_days_before_the_end()
    {
        var ends = Start.AddDays(14);

        var steps = TrialReminders.Plan(ends, Start, [3]);

        steps.Where(s => s.Kind == TrialStepKind.Email).Select(s => s.DaysBefore).Should().Equal(3, 0);
        steps.Single(s => s.Kind == TrialStepKind.Notice).At.Should().Be(ends.AddDays(-7));
    }

    [Fact]
    public void A_trial_already_under_way_skips_the_emails_whose_moment_has_passed_and_shows_the_bell_notice_at_once()
    {
        var ends = Start.AddDays(14);
        var now = ends.AddDays(-5);

        var steps = TrialReminders.Plan(ends, now, null);

        steps.Where(s => s.Kind == TrialStepKind.Email).Select(s => s.DaysBefore).Should().Equal(1, 0); // the seven day email's moment has passed
        steps.Single(s => s.Kind == TrialStepKind.Notice).At.Should().Be(now, "fewer than seven days are left, so the notice is due now");
    }

    [Fact]
    public void A_trial_that_ended_without_a_plan_gets_only_the_ended_notice_and_at_once()
    {
        var ends = Start.AddDays(14);
        var now = ends.AddDays(3);

        var steps = TrialReminders.Plan(ends, now, null);

        steps.Should().ContainSingle().Which.Should().Be(new TrialStep(TrialStepKind.EndedNotice, 0, now));
    }

    [Fact]
    public void A_trial_shorter_than_the_notice_window_shows_the_notice_at_once_and_emails_only_what_is_left()
    {
        var ends = Start.AddDays(3);

        var steps = TrialReminders.Plan(ends, Start, null);

        steps.Where(s => s.Kind == TrialStepKind.Email).Select(s => s.DaysBefore).Should().Equal(1, 0);
        steps.Single(s => s.Kind == TrialStepKind.Notice).At.Should().Be(Start);
    }

    [Fact]
    public void A_reminder_acts_while_no_subscription_has_started_and_the_trial_is_the_one_it_was_scheduled_for()
    {
        var account = Trial();

        TrialReminders.Applies(account, account.TrialEndsAt).Should().BeTrue();
    }

    [Theory]
    [InlineData(SubscriptionStatus.Active)]
    [InlineData(SubscriptionStatus.PastDue)]
    [InlineData(SubscriptionStatus.Ended)]
    public void A_reminder_that_fires_after_a_subscription_started_does_nothing(SubscriptionStatus status)
    {
        var account = Trial();
        var scheduledFor = account.TrialEndsAt;
        account.Apply(Start.AddDays(2), "cus_fictional", "sub_fictional", "starter", status, Start.AddDays(32), false, Start.AddDays(2));

        TrialReminders.Applies(account, scheduledFor).Should().BeFalse();
    }

    [Fact]
    public void A_reminder_for_a_trial_that_was_changed_does_nothing()
    {
        var account = Trial();

        TrialReminders.Applies(account, account.TrialEndsAt.AddDays(-7)).Should().BeFalse();
    }

    [Fact]
    public void Reminders_are_marked_scheduled_for_the_trial_they_were_scheduled_for_and_only_once()
    {
        var account = Trial();
        account.TrialRemindersScheduledFor.Should().BeNull();

        account.MarkTrialRemindersScheduled(Start).Should().BeTrue();
        account.TrialRemindersScheduledFor.Should().Be(account.TrialEndsAt);
        account.MarkTrialRemindersScheduled(Start.AddHours(1)).Should().BeFalse("a second catch-up changes nothing");
    }

    [Fact]
    public void A_configured_list_replaces_the_default_instead_of_adding_to_it()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Trial:RemindDaysBefore:0"] = "3" })
            .Build();
        var options = new BillingOptions();

        configuration.Bind(options);

        TrialReminders.Days(options.Trial.RemindDaysBefore).Should().Equal(3);
    }

    [Fact]
    public void The_options_refuse_a_reminder_day_below_one_in_every_mode()
    {
        var options = new BillingOptions { Trial = new BillingTrialOptions { RemindDaysBefore = [3, 0] } };

        var result = new BillingTrialGate().Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("RemindDaysBefore").And.Contain("1 or more");
        new BillingTrialGate().Validate(null, new BillingOptions { Trial = new BillingTrialOptions { RemindDaysBefore = [3] } }).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void The_email_says_the_plan_the_date_what_happens_and_where_to_choose_a_plan()
    {
        var ends = new DateTimeOffset(2026, 12, 10, 12, 0, 0, TimeSpan.Zero);

        var message = TrialEndingEmail.Compose("owner@example.com", "Brightwater Studio", "Growth", ends, 7, new Uri("https://app.example.com/settings/billing"));

        message.To.Should().Be("owner@example.com");
        message.Subject.Should().Be("Your ScreenHire trial ends in 7 days");
        message.TextBody.Should().Contain("Brightwater Studio").And.Contain("in 7 days").And.Contain("10 December 2026 (UTC)").And.Contain("Growth plan")
            .And.Contain("Nothing is deleted").And.Contain("open jobs stay open").And.Contain("you will need a plan")
            .And.Contain("https://app.example.com/settings/billing");
        message.HtmlBody.Should().Contain("href=\"https://app.example.com/settings/billing\"");
        message.TextBody.Should().NotContain("—").And.NotContain("–");
    }

    [Theory]
    [InlineData(1, "Your ScreenHire trial ends tomorrow", "ends tomorrow")]
    [InlineData(0, "Your ScreenHire trial ends today", "ends today")]
    public void The_email_for_the_last_day_and_the_day_it_ends_say_tomorrow_and_today(int daysBefore, string subject, string sentence)
    {
        var message = TrialEndingEmail.Compose("owner@example.com", "Brightwater Studio", null, new DateTimeOffset(2026, 12, 10, 12, 0, 0, TimeSpan.Zero), daysBefore, new Uri("https://app.example.com/settings/billing"));

        message.Subject.Should().Be(subject);
        message.TextBody.Should().Contain(sentence).And.NotContain("During the trial you have");
    }

    [Fact]
    public void A_reminders_key_names_the_organization_the_trial_and_the_day_so_a_changed_trial_is_not_swallowed_by_the_old_claim()
    {
        var ends = new DateTimeOffset(2026, 12, 10, 12, 0, 0, TimeSpan.Zero);

        var seven = RemindTrialEnding.For(Organization, ends, 7);

        seven.Key.Should().Contain(Organization.ToString("N"));
        seven.Key.Should().NotBe(RemindTrialEnding.For(Organization, ends, 1).Key);
        seven.Key.Should().NotBe(RemindTrialEnding.For(Organization, ends.AddDays(2), 7).Key);
        EmailTrialEnding.For(seven, Guid.CreateVersion7()).Key.Should().NotBe(EmailTrialEnding.For(seven, Guid.CreateVersion7()).Key);
    }
}
