namespace ScreenHire.Domain.Billing;

/// <summary>What one scheduled step of a trial's reminders does.</summary>
public enum TrialStepKind
{
    /// <summary>Emails the Owners that the trial ends.</summary>
    Email,

    /// <summary>Puts "your trial ends in N days" in the Owners' bell.</summary>
    Notice,

    /// <summary>Swaps that for "your trial has ended".</summary>
    EndedNotice,
}

/// <summary>One step of a trial's reminders: what to do, and when. <paramref name="DaysBefore"/> is 0 for the day the trial ends.</summary>
public sealed record TrialStep(TrialStepKind Kind, int DaysBefore, DateTimeOffset At);

/// <summary>
/// When an organization on a free trial is reminded that it ends (docs/architecture/billing.md, "Trial reminders"), as pure rules.
/// The worker schedules the steps once, when the trial starts, and each step checks <see cref="Applies"/> when it fires.
/// </summary>
public static class TrialReminders
{
    /// <summary>The bell starts telling the Owners this many days before the trial ends.</summary>
    public const int NoticeDays = 7;

    /// <summary>The days before the end that get an email when nothing is configured.</summary>
    public static IReadOnlyList<int> DefaultDaysBefore { get; } = [7, 1];

    /// <summary>
    /// The days before the end to email, latest first and each once. Nothing configured means <see cref="DefaultDaysBefore"/>. A day
    /// of zero or less is dropped: the day the trial ends is always reminded and is not a setting.
    /// </summary>
    public static IReadOnlyList<int> Days(IEnumerable<int>? configured)
    {
        var days = (configured ?? []).Where(d => d > 0).Distinct().OrderDescending().ToList();
        return days.Count == 0 ? DefaultDaysBefore : days;
    }

    /// <summary>
    /// Whether a step scheduled for a trial that ends at <paramref name="trialEndsAt"/> still acts on <paramref name="account"/>:
    /// no subscription has started, and the trial is still the one the step was scheduled for.
    /// </summary>
    public static bool Applies(BillingAccount account, DateTimeOffset trialEndsAt)
    {
        ArgumentNullException.ThrowIfNull(account);
        return account.Status == SubscriptionStatus.None && account.TrialEndsAt == trialEndsAt;
    }

    /// <summary>
    /// The steps still to come for a trial that ends at <paramref name="trialEndsAt"/>, looked at from <paramref name="now"/>:
    /// an email for each configured day and one when the trial ends, none for a moment already past; the bell's notice
    /// <see cref="NoticeDays"/> days before the end, or at once when fewer days are left; and the swap to "ended" when the trial
    /// ends, or at once when it already has. The steps have no order beyond their times.
    /// </summary>
    public static IReadOnlyList<TrialStep> Plan(DateTimeOffset trialEndsAt, DateTimeOffset now, IEnumerable<int>? configuredDays)
    {
        var steps = new List<TrialStep>();
        foreach (var days in Days(configuredDays).Append(0))
        {
            var at = trialEndsAt.AddDays(-days);
            if (at > now)
            {
                steps.Add(new TrialStep(TrialStepKind.Email, days, at));
            }
        }

        if (trialEndsAt > now)
        {
            var noticeAt = trialEndsAt.AddDays(-NoticeDays);
            steps.Add(new TrialStep(TrialStepKind.Notice, NoticeDays, noticeAt > now ? noticeAt : now));
        }

        steps.Add(new TrialStep(TrialStepKind.EndedNotice, 0, trialEndsAt > now ? trialEndsAt : now));
        return steps;
    }
}
