using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Billing;

/// <summary>
/// One row of an organization's AI credit ledger (docs/architecture/billing.md, "AI credits"; ADR-0032). A grant is positive and a
/// debit negative, in micro-dollars, and the balance is the sum. Append-only: the application role can neither update nor delete a row.
/// <see cref="SourceKey"/> is unique within the organization, so each effect lands once: one grant per purchase, one debit per call,
/// one expiry per grant.
/// </summary>
public sealed class CreditEntry : ITenantOwned
{
    public const int MaxSourceKeyLength = 80;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public CreditEntryKind Kind { get; init; }

    /// <summary>Micro-dollars: positive for a grant, negative for a debit.</summary>
    public long Micros { get; init; }

    public DateTimeOffset At { get; init; }

    /// <summary>When the rest of a grant expires, or null when it never does. Debits have none.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>What the row is for, unique in the organization (<see cref="CreditSourceKeys"/>).</summary>
    public required string SourceKey { get; init; }

    /// <summary>The purchase a grant comes from.</summary>
    public Guid? PurchaseId { get; init; }

    /// <summary>The ledger call (<c>ai_usage_calls</c>) a usage debit pays for. A plain id: the ledger outlives an erased candidate.</summary>
    public Guid? AiCallId { get; init; }

    /// <summary>The consistency check whose calls a usage debit pays for, so a deduction is traceable to the check that used it.</summary>
    public Guid? ConsistencyCheckId { get; init; }

    /// <summary>The application that check was for. A plain id with no foreign key, as on <c>ai_usage_calls</c>.</summary>
    public Guid? ApplicationId { get; init; }

    /// <summary>The grant an expiry writes off what is left of.</summary>
    public Guid? GrantEntryId { get; init; }
}

public enum CreditEntryKind
{
    /// <summary>What every organization starts with: the trial's AI allowance (<c>Ai:TrialAllowance</c>).</summary>
    StartingCredit,

    /// <summary>Credits bought through Checkout.</summary>
    Purchase,

    /// <summary>Credits added by auto-reload.</summary>
    AutoReload,

    /// <summary>One AI call on ScreenHire's key, at the provider's price plus the margin.</summary>
    Usage,

    /// <summary>What was left of a grant when it expired.</summary>
    Expiry,
}

/// <summary>The <see cref="CreditEntry.SourceKey"/> of each kind of row.</summary>
public static class CreditSourceKeys
{
    public const string Starting = "starting";

    public static string Purchase(Guid purchaseId) => $"purchase:{purchaseId:N}";

    public static string Call(Guid aiCallId) => $"call:{aiCallId:N}";

    public static string Expiry(Guid grantEntryId) => $"expiry:{grantEntryId:N}";
}

/// <summary>
/// One attempt to buy credits, through Checkout or by auto-reload: the purchase history, and the record a grant is keyed on. A grant
/// is made only when the provider's signed event says the payment succeeded; this row alone grants nothing.
/// </summary>
public sealed class CreditPurchase : ITenantOwned
{
    public const int MaxProviderIdLength = 100;

    public const int MaxFailureCodeLength = 60;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public CreditPurchaseSource Source { get; init; }

    /// <summary>The credits bought, in US dollars to the cent. Tax, when charged, is on top and is not credit.</summary>
    public decimal Amount { get; init; }

    public CreditPurchaseStatus Status { get; private set; } = CreditPurchaseStatus.Pending;

    /// <summary>The provider's id for the payment: a Checkout session's payment intent, or an auto-reload's invoice.</summary>
    public string? ProviderPaymentId { get; private set; }

    /// <summary>The provider's reason a charge was refused (<c>card_declined</c>, <c>authentication_required</c>).</summary>
    public string? FailureCode { get; private set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>
    /// Records that the payment succeeded. False when it was already recorded. A purchase recorded as failed can still be paid: the
    /// provider's signed confirmation is the truth, and money it took is always credited.
    /// </summary>
    public bool MarkPaid(string? providerPaymentId, DateTimeOffset now)
    {
        if (Status == CreditPurchaseStatus.Paid)
        {
            return false;
        }

        Status = CreditPurchaseStatus.Paid;
        ProviderPaymentId = providerPaymentId ?? ProviderPaymentId;
        CompletedAt = now;
        return true;
    }

    /// <summary>Records that the provider refused the charge. False when the purchase was already settled.</summary>
    public bool MarkFailed(string? providerPaymentId, string? failureCode, DateTimeOffset now)
    {
        if (Status != CreditPurchaseStatus.Pending)
        {
            return false;
        }

        Status = CreditPurchaseStatus.Failed;
        ProviderPaymentId = providerPaymentId ?? ProviderPaymentId;
        FailureCode = failureCode is { Length: > MaxFailureCodeLength } ? failureCode[..MaxFailureCodeLength] : failureCode;
        CompletedAt = now;
        return true;
    }

    /// <summary>The provider's id, once it is known and before the payment settles (an auto-reload's invoice).</summary>
    public void RecordProviderPayment(string providerPaymentId) => ProviderPaymentId = providerPaymentId;
}

public enum CreditPurchaseSource
{
    Checkout,
    AutoReload,
}

public enum CreditPurchaseStatus
{
    /// <summary>Started, and not yet confirmed or refused by the provider. An abandoned Checkout stays here.</summary>
    Pending,

    Paid,

    Failed,
}

/// <summary>
/// An organization's credit settings: the low-balance alert and auto-reload. One row per organization, apart from
/// <c>billing_accounts</c> because that row's concurrency token races with Stripe's webhooks.
/// </summary>
public sealed class CreditSettings : ITenantOwned
{
    public const int MaxPaymentMethodLength = 100;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    /// <summary>The Owners are told once the balance falls below this many US dollars.</summary>
    public decimal LowBalanceAlert { get; set; }

    public bool AutoReloadEnabled { get; private set; }

    /// <summary>Auto-reload runs when the balance falls below this many US dollars.</summary>
    public decimal? AutoReloadBelow { get; private set; }

    /// <summary>The US dollars one auto-reload adds.</summary>
    public decimal? AutoReloadAmount { get; private set; }

    /// <summary>The most US dollars auto-reload may charge in one UTC calendar month.</summary>
    public decimal? AutoReloadMonthlyMax { get; private set; }

    /// <summary>The card the last Checkout saved for off-session charges, which auto-reload uses.</summary>
    public string? PaymentMethodId { get; private set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public bool HasSavedCard => !string.IsNullOrEmpty(PaymentMethodId);

    public static CreditSettings For(Guid organizationId, decimal lowBalanceAlert, DateTimeOffset now) => new()
    {
        OrganizationId = organizationId,
        LowBalanceAlert = lowBalanceAlert,
        UpdatedAt = now,
    };

    public void SetAutoReload(bool enabled, decimal? below, decimal? amount, decimal? monthlyMax, DateTimeOffset now)
    {
        AutoReloadEnabled = enabled;
        AutoReloadBelow = below;
        AutoReloadAmount = amount;
        AutoReloadMonthlyMax = monthlyMax;
        UpdatedAt = now;
    }

    /// <summary>Turns auto-reload off and keeps its amounts, so turning it on again is one switch.</summary>
    public void StopAutoReload(DateTimeOffset now)
    {
        AutoReloadEnabled = false;
        UpdatedAt = now;
    }

    public void SaveCard(string paymentMethodId, DateTimeOffset now)
    {
        PaymentMethodId = paymentMethodId;
        UpdatedAt = now;
    }
}

/// <summary>A ledger row as the pure rules read it.</summary>
public readonly record struct CreditMovement(Guid Id, DateTimeOffset At, long Micros, DateTimeOffset? ExpiresAt, CreditEntryKind Kind, Guid? ExpiresGrant = null);

/// <summary>What is left of an expired grant, which the expiry run writes off.</summary>
public readonly record struct ExpiringCredit(Guid GrantId, long Micros);

/// <summary>
/// Works out what each expired grant has left (pure). The ledger is replayed in time order: a grant opens a lot, and a debit uses
/// the lot that expires first (a lot that never expires last, then the oldest). A debit with no lot left is a shortfall, which the next
/// grant repays first: checks that run at the same moment can take the balance a little below zero. A lot past its expiry is gone
/// from that moment; one with an expiry already recorded is not returned again.
/// </summary>
public static class CreditLots
{
    public static IReadOnlyList<ExpiringCredit> Expiring(IEnumerable<CreditMovement> ledger, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        var rows = ledger.ToList();
        var recorded = rows.Where(r => r.Kind == CreditEntryKind.Expiry && r.ExpiresGrant is not null).Select(r => r.ExpiresGrant!.Value).ToHashSet();

        // Each grant's expiry is a step of its own at the moment it expires; a recorded expiry row is that step, not a debit.
        var steps = new List<(DateTimeOffset At, int Order, Guid Id, CreditMovement Row, bool IsExpiry)>();
        foreach (var row in rows)
        {
            if (row.Kind == CreditEntryKind.Expiry)
            {
                continue;
            }

            steps.Add((row.At, 1, row.Id, row, false));
            if (row.Micros > 0 && row.ExpiresAt is { } expires && expires <= now)
            {
                // Order 0: a lot that expires at the moment of a debit is gone before the debit.
                steps.Add((expires, 0, row.Id, row, true));
            }
        }

        var lots = new List<Lot>();
        long shortfall = 0;
        var expiring = new List<ExpiringCredit>();
        foreach (var step in steps.OrderBy(s => s.At).ThenBy(s => s.Order).ThenBy(s => s.Id))
        {
            if (step.IsExpiry)
            {
                var lot = lots.Find(l => l.GrantId == step.Id);
                if (lot is null)
                {
                    continue;
                }

                lots.Remove(lot);
                if (lot.Remaining > 0 && !recorded.Contains(lot.GrantId))
                {
                    expiring.Add(new ExpiringCredit(lot.GrantId, lot.Remaining));
                }

                continue;
            }

            var row = step.Row;
            if (row.Micros > 0)
            {
                var repaid = Math.Min(shortfall, row.Micros);
                shortfall -= repaid;
                lots.Add(new Lot(row.Id, row.At, row.ExpiresAt, row.Micros - repaid));
                continue;
            }

            var debit = -row.Micros;
            foreach (var lot in lots.OrderBy(l => l.ExpiresAt ?? DateTimeOffset.MaxValue).ThenBy(l => l.At).ThenBy(l => l.GrantId))
            {
                if (debit == 0)
                {
                    break;
                }

                var used = Math.Min(debit, lot.Remaining);
                lot.Remaining -= used;
                debit -= used;
            }

            shortfall += debit;
        }

        return expiring;
    }

    private sealed class Lot(Guid grantId, DateTimeOffset at, DateTimeOffset? expiresAt, long remaining)
    {
        public Guid GrantId { get; } = grantId;

        public DateTimeOffset At { get; } = at;

        public DateTimeOffset? ExpiresAt { get; } = expiresAt;

        public long Remaining { get; set; } = remaining;
    }
}

/// <summary>Why auto-reload did or did not run.</summary>
public enum AutoReloadOutcome
{
    Reload,
    Off,
    NoCard,
    AboveThreshold,
    AlreadyReloading,
    MonthlyMaximum,
}

public readonly record struct AutoReloadDecision(AutoReloadOutcome Outcome, decimal Amount = 0m)
{
    public bool Reloads => Outcome == AutoReloadOutcome.Reload;
}

/// <summary>
/// Whether auto-reload adds credits now (pure): it is on, a card is saved, the balance is below the threshold, no reload is already
/// waiting for its payment, and one more would stay within the monthly maximum, which counts the month's auto-reloads that are paid
/// or still waiting.
/// </summary>
public static class AutoReload
{
    public static AutoReloadDecision Decide(CreditSettings? settings, long balanceMicros, decimal reloadedThisMonth, bool reloadPending)
    {
        if (settings is not { AutoReloadEnabled: true, AutoReloadBelow: { } below, AutoReloadAmount: { } amount })
        {
            return new AutoReloadDecision(AutoReloadOutcome.Off);
        }

        if (!settings.HasSavedCard)
        {
            return new AutoReloadDecision(AutoReloadOutcome.NoCard);
        }

        if (balanceMicros >= AiCharge.ToMicros(below))
        {
            return new AutoReloadDecision(AutoReloadOutcome.AboveThreshold);
        }

        if (reloadPending)
        {
            return new AutoReloadDecision(AutoReloadOutcome.AlreadyReloading);
        }

        if (settings.AutoReloadMonthlyMax is { } max && reloadedThisMonth + amount > max)
        {
            return new AutoReloadDecision(AutoReloadOutcome.MonthlyMaximum);
        }

        return new AutoReloadDecision(AutoReloadOutcome.Reload, amount);
    }
}

public enum CreditAlert
{
    /// <summary>The balance fell below the organization's low-balance alert.</summary>
    Low,

    /// <summary>The balance is at or below zero: the AI check pauses.</summary>
    Empty,

    /// <summary>An auto-reload's charge was refused, and auto-reload turned off.</summary>
    ReloadFailed,
}

/// <summary>
/// Which alert the balance calls for (pure). None while a reload is starting, because that is what the alerts are for. Auto-reload
/// being on is not enough to stay quiet: a reload that the monthly maximum or a missing card stopped must not leave the organization
/// at zero with no warning.
/// </summary>
public static class CreditAlerts
{
    public static CreditAlert? Due(long balanceMicros, decimal lowBalanceAlert, bool reloadStarting)
    {
        if (reloadStarting)
        {
            return null;
        }

        if (balanceMicros <= 0)
        {
            return CreditAlert.Empty;
        }

        return balanceMicros < AiCharge.ToMicros(lowBalanceAlert) ? CreditAlert.Low : null;
    }
}
