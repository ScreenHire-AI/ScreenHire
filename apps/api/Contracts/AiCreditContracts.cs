namespace ScreenHire.Api.Contracts;

/// <summary>
/// The organization's prepaid AI credits (ADR-0032, docs/architecture/billing.md "AI credits"). <c>available</c> is false where no
/// payment provider is set up, and then nothing else is set. <c>balance</c> is in US dollars, below zero when checks that ran at the
/// same moment overshot it. <c>paused</c>: the balance is at or below zero and ScreenHire's key runs the AI check, so it is paused.
/// <c>ownKey</c>: the organization's own key runs it and credits are not drawn. <c>amounts</c> are the fixed amounts on offer and
/// <c>expiryMonths</c> how long a credit lasts (null: never). <c>hasSavedCard</c>: a purchase saved a card, which auto-reload needs.
/// </summary>
public sealed record AiCreditsView(
    bool Available,
    decimal Balance,
    bool Paused,
    bool OwnKey,
    IReadOnlyList<decimal> Amounts,
    int? ExpiryMonths,
    decimal LowBalanceAlert,
    AiAutoReload AutoReload,
    bool HasSavedCard);

/// <summary>
/// Auto-reload: when the balance falls below <c>below</c>, add <c>amount</c>, never more than <c>monthlyMax</c> in a UTC month.
/// <c>reloadedThisMonth</c> is what it has charged this month. Off by default; a refused charge turns it off.
/// </summary>
public sealed record AiAutoReload(bool Enabled, decimal? Below, decimal? Amount, decimal? MonthlyMax, decimal ReloadedThisMonth);

/// <summary>Where a credit came from.</summary>
public enum AiCreditSource
{
    /// <summary>The credit every organization starts with.</summary>
    StartingCredit,

    /// <summary>Bought through Checkout.</summary>
    Checkout,

    AutoReload,
}

/// <summary>How a credit purchase went.</summary>
public enum AiCreditPurchaseStatus
{
    Paid,

    /// <summary>Given, not bought: the starting credit.</summary>
    Included,

    /// <summary>The payment was refused, and nothing was added.</summary>
    Failed,
}

/// <summary>One credit added, or an attempt that failed: when, how much in US dollars, where from, and when it expires.</summary>
public sealed record AiCreditPurchase(Guid Id, DateTimeOffset Date, decimal Amount, AiCreditSource Source, AiCreditPurchaseStatus Status, DateTimeOffset? ExpiresAt);

/// <summary>The credits added and the purchases that failed, newest first.</summary>
public sealed record AiCreditPurchaseList(IReadOnlyList<AiCreditPurchase> Purchases);

/// <summary>What took credits.</summary>
public enum AiCreditDeductionKind
{
    /// <summary>One AI check's calls on ScreenHire's key.</summary>
    Check,

    /// <summary>What was left of a credit when it expired.</summary>
    Expiry,
}

/// <summary>
/// One deduction from the balance, in US dollars: an AI check's calls (<c>calls</c> of them) for the application <c>applicationId</c>,
/// whose candidate is <c>candidateName</c> while the application still exists, or a credit's unused rest when it expired.
/// </summary>
public sealed record AiCreditDeduction(
    string Id, DateTimeOffset Date, AiCreditDeductionKind Kind, decimal Amount, int Calls, Guid? ApplicationId, string? CandidateName);

/// <summary>Deductions newest first. <c>next</c> is the cursor of the page after this one (<c>before</c>), null on the last page.</summary>
public sealed record AiCreditDeductionList(IReadOnlyList<AiCreditDeduction> Deductions, DateTimeOffset? Next);

/// <summary>Buy one of the fixed amounts on offer, in US dollars.</summary>
public sealed record StartAiCreditCheckoutRequest(decimal Amount);

/// <summary>The low-balance alert and auto-reload, in US dollars to the cent.</summary>
public sealed record UpdateAiCreditSettingsRequest(decimal LowBalanceAlert, UpdateAiAutoReload AutoReload);

/// <summary>Auto-reload's settings. Turning it on needs all three amounts and a saved card.</summary>
public sealed record UpdateAiAutoReload(bool Enabled, decimal? Below = null, decimal? Amount = null, decimal? MonthlyMax = null);
