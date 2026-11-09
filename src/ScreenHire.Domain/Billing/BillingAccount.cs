using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Billing;

/// <summary>
/// An organization's billing (docs/architecture/billing.md): when its trial ends and the subscription the provider
/// reports. One per organization, created when billing is on. The provider owns the truth about the subscription;
/// this row is the last state it told us, which is all the publish check reads.
/// </summary>
public sealed class BillingAccount : ITenantOwned
{
    public const int MaxKeyLength = 40;

    public const int MaxProviderIdLength = 100;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    /// <summary>When the free trial ends. Fixed at sign-up, so changing the configured length never moves it.</summary>
    public DateTimeOffset TrialEndsAt { get; private set; }

    /// <summary>The plan key the subscription is on (<c>starter</c>, <c>growth</c>, <c>unlimited</c>), or null before one.</summary>
    public string? PlanKey { get; private set; }

    public SubscriptionStatus Status { get; private set; } = SubscriptionStatus.None;

    public string? ProviderCustomerId { get; private set; }

    public string? ProviderSubscriptionId { get; private set; }

    /// <summary>When the current period ends, which is the next payment unless the subscription is set to cancel.</summary>
    public DateTimeOffset? CurrentPeriodEnd { get; private set; }

    /// <summary>The subscription stays active until <see cref="CurrentPeriodEnd"/> and then ends.</summary>
    public bool CancelsAtPeriodEnd { get; private set; }

    /// <summary>The provider timestamp of the newest event applied, so a late older event cannot undo a newer one.</summary>
    public DateTimeOffset? LastEventAt { get; private set; }

    /// <summary>
    /// The <see cref="TrialEndsAt"/> the trial reminders were scheduled for, or null before they were. Scheduling them is then
    /// a no-op, so a redelivered message or a second catch-up never schedules a trial's reminders twice.
    /// </summary>
    public DateTimeOffset? TrialRemindersScheduledFor { get; private set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Records that the reminders for the current trial are scheduled. False when they already were.</summary>
    public bool MarkTrialRemindersScheduled(DateTimeOffset now)
    {
        if (TrialRemindersScheduledFor == TrialEndsAt)
        {
            return false;
        }

        TrialRemindersScheduledFor = TrialEndsAt;
        UpdatedAt = now;
        return true;
    }

    public static BillingAccount StartTrial(Guid organizationId, DateTimeOffset now, int trialDays) => new()
    {
        OrganizationId = organizationId,
        TrialEndsAt = now.AddDays(trialDays),
        CreatedAt = now,
        UpdatedAt = now,
    };

    /// <summary>
    /// Applies what the provider reported. False when the event is older than one already applied. Fields the event does
    /// not carry (null) keep their value.
    /// </summary>
    public bool Apply(
        DateTimeOffset eventAt, string? customerId, string? subscriptionId, string? planKey, SubscriptionStatus? status,
        DateTimeOffset? currentPeriodEnd, bool? cancelsAtPeriodEnd, DateTimeOffset now)
    {
        if (LastEventAt is { } last && eventAt < last)
        {
            return false;
        }

        LastEventAt = eventAt;
        ProviderCustomerId = customerId ?? ProviderCustomerId;
        ProviderSubscriptionId = subscriptionId ?? ProviderSubscriptionId;
        PlanKey = planKey ?? PlanKey;
        Status = status ?? Status;
        CurrentPeriodEnd = currentPeriodEnd ?? CurrentPeriodEnd;
        CancelsAtPeriodEnd = cancelsAtPeriodEnd ?? CancelsAtPeriodEnd;
        UpdatedAt = now;
        return true;
    }
}

/// <summary>The subscription as the provider last reported it.</summary>
public enum SubscriptionStatus
{
    /// <summary>No subscription yet: the trial, or nothing.</summary>
    None,

    /// <summary>Paid up, or in the provider's own trial.</summary>
    Active,

    /// <summary>A payment failed and the provider is retrying. Publishing still works.</summary>
    PastDue,

    /// <summary>Cancelled, unpaid or never completed.</summary>
    Ended,
}

/// <summary>A provider event already applied, so a redelivery changes nothing. One row per event id and organization.</summary>
public sealed class BillingEventRecord : ITenantOwned
{
    public const int MaxEventIdLength = 100;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public string EventId { get; init; } = "";

    public DateTimeOffset ReceivedAt { get; init; }
}
