namespace ScreenHire.Domain.Billing;

/// <summary>Where an organization stands, for the Billing page and the publish check.</summary>
public enum BillingState
{
    Trialing,
    Active,
    PastDue,
    Ended,
}

public enum PublishDecisionKind
{
    Allowed,
    LimitReached,
    SubscriptionEnded,
}

/// <summary>Whether a draft may open, and when not, the figures the message needs.</summary>
public readonly record struct PublishDecision(PublishDecisionKind Kind, int? Limit = null, int OpenJobs = 0)
{
    public bool IsAllowed => Kind == PublishDecisionKind.Allowed;
}

/// <summary>
/// The rules that turn a billing account into what an organization may do (ADR-0029). Pure: the caller supplies the
/// clock, the plan's limit and the count. Only opening a draft is ever refused; everything else stays readable and
/// open jobs stay open.
/// </summary>
public static class Entitlement
{
    public static BillingState StateOf(BillingAccount account, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(account);
        return account.Status switch
        {
            SubscriptionStatus.Active => BillingState.Active,
            SubscriptionStatus.PastDue => BillingState.PastDue,
            SubscriptionStatus.Ended => BillingState.Ended,
            _ => now < account.TrialEndsAt ? BillingState.Trialing : BillingState.Ended,
        };
    }

    /// <summary>
    /// The plan whose limit applies: the subscription's while there is one, the trial's before that. Null when the
    /// account has ended, or the subscription names no plan.
    /// </summary>
    public static string? EffectivePlanKey(BillingAccount account, DateTimeOffset now, string trialPlanKey) =>
        StateOf(account, now) switch
        {
            BillingState.Trialing => trialPlanKey,
            BillingState.Active or BillingState.PastDue => account.PlanKey,
            _ => null,
        };

    /// <summary>
    /// Whether a draft may open. <paramref name="limit"/> is the plan's active job limit (null: none, unlimited or not set) and
    /// <paramref name="openJobs"/> the jobs open now across the whole organization.
    /// </summary>
    public static PublishDecision CanPublishDraft(BillingAccount account, DateTimeOffset now, int? limit, int openJobs)
    {
        if (StateOf(account, now) == BillingState.Ended)
        {
            return new PublishDecision(PublishDecisionKind.SubscriptionEnded, limit, openJobs);
        }

        return limit is { } cap && openJobs >= cap
            ? new PublishDecision(PublishDecisionKind.LimitReached, cap, openJobs)
            : new PublishDecision(PublishDecisionKind.Allowed, limit, openJobs);
    }
}
