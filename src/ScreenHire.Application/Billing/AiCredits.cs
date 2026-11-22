using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Applications;
using ScreenHire.Application.Messaging;
using ScreenHire.Application.Notifications;
using ScreenHire.Application.Verification;
using ScreenHire.Domain.Billing;
using ScreenHire.Domain.Notifications;
using ScreenHire.Domain.Organizations;
using ScreenHire.Domain.Verification;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Verification.Llm;
using Wolverine;
using Wolverine.Attributes;

namespace ScreenHire.Application.Billing;

/// <summary>
/// The AI credit ledger (ADR-0032, docs/architecture/billing.md "AI credits"): the balance, the starting credit, and the debits a
/// check's calls on ScreenHire's key make. Every query runs under the organization's row-level security, so none names it.
/// </summary>
public static class AiCreditLedger
{
    /// <summary>The balance in micro-dollars: the sum of the ledger. Below zero when checks that ran together overshot it.</summary>
    public static Task<long> BalanceMicrosAsync(ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.CreditEntries.AsNoTracking().SumAsync(e => e.Micros, cancellationToken);
    }

    /// <summary>
    /// Whether the AI check pauses for lack of credits: credits are on, ScreenHire's key runs the check, and the balance is at or below
    /// zero. An organization's own key never touches the balance. The starting credit is granted first, so an organization older than
    /// credits is not paused for having none yet.
    /// </summary>
    public static async Task<bool> PausesAsync(
        ScreenHireDbContext db, VerificationSettings? settings, BillingOptions options, Guid organizationId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.CreditsEnabled || settings is { UsesOwnKey: true })
        {
            return false;
        }

        await EnsureStartingCreditAsync(db, organizationId, options, now, cancellationToken);
        return await BalanceMicrosAsync(db, cancellationToken) <= 0;
    }

    /// <summary>
    /// Grants the starting credit (<c>Ai:TrialAllowance</c>) once. One statement that does nothing when the organization has it, so the
    /// onboarding, the check and the Billing page can each ask, at once if need be. Nothing while credits are off or the credit is zero.
    /// </summary>
    public static async Task EnsureStartingCreditAsync(
        ScreenHireDbContext db, Guid organizationId, BillingOptions options, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(options);
        var micros = AiCharge.ToMicros(options.Ai.TrialAllowance);
        if (!options.CreditsEnabled || micros <= 0
            || await db.CreditEntries.AsNoTracking().AnyAsync(e => e.SourceKey == CreditSourceKeys.Starting, cancellationToken))
        {
            return;
        }

        var kind = nameof(CreditEntryKind.StartingCredit);
        var expiresAt = options.Credits.ExpiresAt(now);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO credit_entries (id, organization_id, kind, micros, at, expires_at, source_key)
            VALUES ({Guid.CreateVersion7()}, {organizationId}, {kind}, {micros}, {now}, {expiresAt}, {CreditSourceKeys.Starting})
            ON CONFLICT (organization_id, source_key) DO NOTHING
            """,
            cancellationToken);
    }

    /// <summary>
    /// The debits for a check's calls: one per call on ScreenHire's key with a known cost, at the provider's price plus
    /// <paramref name="margin"/> (<see cref="AiCharge.Micros"/>), keyed on the call so it is drawn once. A call on the organization's own
    /// key, or with no known cost, draws nothing. The caller adds them in the save that records the calls.
    /// </summary>
    public static IEnumerable<CreditEntry> UsageEntries(IEnumerable<AiUsageCall> calls, decimal margin, Guid checkId, Guid applicationId)
    {
        ArgumentNullException.ThrowIfNull(calls);
        foreach (var call in calls)
        {
            var micros = AiCharge.Micros(call.Cost, margin);
            if (call.KeySource != AiKeySource.BuiltIn || micros <= 0)
            {
                continue;
            }

            yield return new CreditEntry
            {
                OrganizationId = call.OrganizationId,
                Kind = CreditEntryKind.Usage,
                Micros = -micros,
                At = call.OccurredAt,
                SourceKey = CreditSourceKeys.Call(call.Id),
                AiCallId = call.Id,
                ConsistencyCheckId = checkId,
                ApplicationId = applicationId,
            };
        }
    }

    /// <summary>The newest grant's id: alerts and auto-reload are keyed on it, so each refill can alert and reload once.</summary>
    public static Task<Guid> LatestGrantAsync(ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.CreditEntries.AsNoTracking().Where(e => e.Micros > 0).OrderByDescending(e => e.At).ThenByDescending(e => e.Id)
            .Select(e => e.Id).FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>US dollars auto-reload has charged or is charging in the UTC month of <paramref name="now"/>, for the monthly maximum.</summary>
    public static Task<decimal> ReloadedThisMonthAsync(ScreenHireDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var monthStart = AiUsageQueries.MonthStart(now);
        return db.CreditPurchases.AsNoTracking()
            .Where(p => p.Source == CreditPurchaseSource.AutoReload && p.Status != CreditPurchaseStatus.Failed && p.CreatedAt >= monthStart)
            .SumAsync(p => p.Amount, cancellationToken);
    }

    /// <summary>The organization's credit settings, tracked, made with the defaults when it has none yet.</summary>
    public static async Task<CreditSettings> SettingsAsync(
        ScreenHireDbContext db, Guid organizationId, BillingOptions options, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(options);
        if (await db.CreditSettings.SingleOrDefaultAsync(cancellationToken) is { } settings)
        {
            return settings;
        }

        settings = Domain.Billing.CreditSettings.For(organizationId, options.Credits.LowBalanceAlert, now);
        db.CreditSettings.Add(settings);
        return settings;
    }
}

/// <summary>
/// What the balance calls for after a check drew on it: an auto-reload, and the low-balance or empty alert. Each message is keyed on
/// the newest grant, so the checks that finish together send one, and the next refill can send them again.
/// </summary>
public static class AiCreditChecks
{
    public static async Task<List<object>> DueAsync(
        ScreenHireDbContext db, Guid organizationId, BillingOptions options, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(options);
        var due = new List<object>();
        if (!options.CreditsEnabled)
        {
            return due;
        }

        var settings = await db.CreditSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var balance = await AiCreditLedger.BalanceMicrosAsync(db, cancellationToken);
        var latestGrant = await AiCreditLedger.LatestGrantAsync(db, cancellationToken);
        var reloaded = await AiCreditLedger.ReloadedThisMonthAsync(db, now, cancellationToken);
        var pending = await db.CreditPurchases.AsNoTracking()
            .AnyAsync(p => p.Source == CreditPurchaseSource.AutoReload && p.Status == CreditPurchaseStatus.Pending, cancellationToken);
        var reload = AutoReload.Decide(settings, balance, reloaded, pending);
        if (reload.Reloads)
        {
            due.Add(new ReloadAiCredits(organizationId, latestGrant));
        }

        if (CreditAlerts.Due(balance, settings?.LowBalanceAlert ?? options.Credits.LowBalanceAlert, reload.Reloads) is { } alert)
        {
            var key = alert == CreditAlert.Empty ? AiUsageAlertKeys.CreditsEmpty(latestGrant) : AiUsageAlertKeys.CreditsLow(latestGrant);
            if (!await db.AiUsageAlerts.AsNoTracking().AnyAsync(a => a.Key == key, cancellationToken))
            {
                due.Add(new NotifyAiCredits(organizationId, alert, key, AiCharge.Dollars(Math.Max(0, balance))));
            }
        }

        return due;
    }
}

/// <summary>
/// The provider confirmed that a credit purchase was paid: grant it. Sent only by the signed webhook and by the sandbox's Checkout
/// and auto-reload, never by the browser's return to the page (ADR-0032). <paramref name="PaymentIntentId"/> is the payment whose card a
/// Checkout saved for auto-reload.
/// </summary>
public sealed record GrantAiCredits(Guid OrganizationId, Guid PurchaseId, string ProviderPaymentId, string? PaymentIntentId = null) : ITenantMessage
{
    public string IdempotencyKey => $"credits-grant:{PurchaseId:N}";
}

/// <summary>Auto-reload is due: the balance fell below its threshold after the grant <paramref name="AfterGrant"/>.</summary>
public sealed record ReloadAiCredits(Guid OrganizationId, Guid AfterGrant) : ITenantMessage
{
    public string IdempotencyKey => $"credits-reload:{OrganizationId:N}:{AfterGrant:N}";
}

/// <summary>
/// Tells the Owners about the balance: low, run out, or an auto-reload refused. <paramref name="Key"/> is claimed in
/// <c>ai_usage_alerts</c>, so each goes once. <paramref name="Amount"/> is the balance for a low one, the reload's amount for a refusal.
/// </summary>
public sealed record NotifyAiCredits(Guid OrganizationId, CreditAlert Kind, string Key, decimal Amount = 0m) : ITenantMessage;

/// <summary>Writes off what is left of expired credits, for every organization (daily, 04:52 UTC). It fans out one message each.</summary>
public sealed record ExpireAiCredits;

public sealed record ExpireOrganizationAiCredits(Guid OrganizationId, string Day) : ITenantMessage
{
    public string IdempotencyKey => $"credits-expire:{OrganizationId:N}:{Day}";
}

public static partial class GrantAiCreditsHandler
{
    [Deduplicated(ValueSource.InputMember, nameof(GrantAiCredits.IdempotencyKey))]
    public static async Task<OutgoingMessages> Handle(
        GrantAiCredits message,
        ScreenHireDbContext db,
        BillingAccess billing,
        IOptions<BillingOptions> options,
        TimeProvider time,
        ILogger<GrantAiCredits> logger,
        CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();
        var purchase = await db.CreditPurchases.SingleOrDefaultAsync(p => p.Id == message.PurchaseId, cancellationToken);
        if (purchase is null)
        {
            LogUnknownPurchase(logger, message.OrganizationId, message.PurchaseId);
            return outgoing;
        }

        if (purchase.Status == CreditPurchaseStatus.Paid)
        {
            return outgoing;
        }

        var now = time.GetUtcNow();
        var key = CreditSourceKeys.Purchase(purchase.Id);
        // The unique key backs this up: two deliveries that both read the purchase as unpaid insert one grant, and the loser retries.
        if (!await db.CreditEntries.AsNoTracking().AnyAsync(e => e.SourceKey == key, cancellationToken))
        {
            db.CreditEntries.Add(new CreditEntry
            {
                OrganizationId = message.OrganizationId,
                Kind = purchase.Source == CreditPurchaseSource.AutoReload ? CreditEntryKind.AutoReload : CreditEntryKind.Purchase,
                Micros = AiCharge.ToMicros(purchase.Amount),
                At = now,
                ExpiresAt = options.Value.Credits.ExpiresAt(now),
                SourceKey = key,
                PurchaseId = purchase.Id,
            });
        }

        purchase.MarkPaid(message.ProviderPaymentId, now);
        if (message.PaymentIntentId is { } intent && billing.IsConfigured)
        {
            // The card the Checkout saved, for auto-reload. Read from the provider: the webhook carries only the payment's id. A failed read
            // keeps the grant; the Owner can buy again to save a card.
            try
            {
                if (await billing.Provider.SavedPaymentMethodAsync(intent, cancellationToken) is { } card)
                {
                    (await AiCreditLedger.SettingsAsync(db, message.OrganizationId, options.Value, now, cancellationToken)).SaveCard(card, now);
                }
            }
            catch (BillingProviderException e)
            {
                LogCardNotRead(logger, message.OrganizationId, e.Status, e.Code);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        foreach (var kind in (string[])[NotificationKinds.AiCreditsLow, NotificationKinds.AiCreditsEmpty, NotificationKinds.AiCreditsReloadFailed])
        {
            outgoing.Add(new ClearNotification(message.OrganizationId, kind, NotificationKinds.CreditsKey));
        }

        // The checks the empty balance paused run now.
        outgoing.Add(new ResumeOrganizationAiChecks(message.OrganizationId, $"credits-{purchase.Id:N}"));
        outgoing.Add(new BillingChanged(message.OrganizationId));
        LogGranted(logger, message.OrganizationId, purchase.Id, purchase.Amount);
        return outgoing;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Organization {OrganizationId}: a payment was confirmed for credit purchase {PurchaseId}, which does not exist; nothing granted")]
    private static partial void LogUnknownPurchase(ILogger logger, Guid organizationId, Guid purchaseId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Organization {OrganizationId}: the card a credit Checkout saved could not be read ({Status} {Code}); auto-reload needs another purchase")]
    private static partial void LogCardNotRead(ILogger logger, Guid organizationId, int? status, string? code);

    [LoggerMessage(Level = LogLevel.Information, Message = "Organization {OrganizationId}: granted credit purchase {PurchaseId}, {Amount} US dollars")]
    private static partial void LogGranted(ILogger logger, Guid organizationId, Guid purchaseId, decimal amount);
}

/// <summary>
/// Charges the saved card for an auto-reload. The purchase is saved as Pending before the charge, so a charge that times out is
/// retried as the same purchase, with the same idempotency keys at the provider: one charge however often this runs. Not wrapped in
/// the handler's transaction for the same reason. A refusal never throws, because a retry would charge again: it marks the purchase
/// failed, turns auto-reload off and tells the Owners.
/// </summary>
public static partial class ReloadAiCreditsHandler
{
    [NonTransactional]
    [Deduplicated(ValueSource.InputMember, nameof(ReloadAiCredits.IdempotencyKey))]
    public static async Task<OutgoingMessages> Handle(
        ReloadAiCredits message,
        ScreenHireDbContext db,
        BillingAccess billing,
        IOptions<BillingOptions> options,
        TimeProvider time,
        ILogger<ReloadAiCredits> logger,
        CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();
        if (!billing.IsConfigured)
        {
            return outgoing;
        }

        var now = time.GetUtcNow();
        var settings = await db.CreditSettings.SingleOrDefaultAsync(cancellationToken);
        // An auto-reload whose charge never answered (the provider timed out, the worker stopped) is finished first, as the same purchase.
        var purchase = await db.CreditPurchases.SingleOrDefaultAsync(
            p => p.Source == CreditPurchaseSource.AutoReload && p.Status == CreditPurchaseStatus.Pending && p.ProviderPaymentId == null, cancellationToken);
        if (purchase is null)
        {
            var balance = await AiCreditLedger.BalanceMicrosAsync(db, cancellationToken);
            var reloaded = await AiCreditLedger.ReloadedThisMonthAsync(db, now, cancellationToken);
            var pending = await db.CreditPurchases.AsNoTracking()
                .AnyAsync(p => p.Source == CreditPurchaseSource.AutoReload && p.Status == CreditPurchaseStatus.Pending, cancellationToken);
            var decision = AutoReload.Decide(settings, balance, reloaded, pending);
            if (!decision.Reloads)
            {
                LogNotReloaded(logger, message.OrganizationId, decision.Outcome);
                return outgoing;
            }

            purchase = new CreditPurchase
            {
                OrganizationId = message.OrganizationId,
                Source = CreditPurchaseSource.AutoReload,
                Amount = decision.Amount,
                CreatedAt = now,
            };
            db.CreditPurchases.Add(purchase);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException e) when (e.IsUniqueViolation())
            {
                // Another reload is waiting for its payment (the index allows one): that one is the reload.
                return outgoing;
            }
        }

        if (settings is not { AutoReloadEnabled: true, PaymentMethodId: { } card })
        {
            purchase.MarkFailed(null, "auto-reload-off", now);
            await db.SaveChangesAsync(cancellationToken);
            return outgoing;
        }

        var account = await db.BillingAccounts.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var customer = account?.ProviderCustomerId ?? (billing.Mode == BillingMode.Sandbox ? SandboxBillingProvider.CustomerOf(message.OrganizationId) : null);
        if (customer is null)
        {
            purchase.MarkFailed(null, "no-customer", now);
            settings.StopAutoReload(now);
            await db.SaveChangesAsync(cancellationToken);
            outgoing.Add(new NotifyAiCredits(message.OrganizationId, CreditAlert.ReloadFailed, ReloadFailedKey(purchase.Id), purchase.Amount));
            return outgoing;
        }

        // A provider that cannot be reached throws, and Wolverine retries: this purchase again, so the same charge.
        var charge = await billing.Provider.ChargeCreditsAsync(
            new CreditChargeRequest(message.OrganizationId, purchase.Id, purchase.Amount, customer, card), cancellationToken);
        if (charge.Status == CreditChargeStatus.Declined)
        {
            purchase.MarkFailed(charge.PaymentId, charge.FailureCode, now);
            settings.StopAutoReload(now);
            await db.SaveChangesAsync(cancellationToken);
            outgoing.Add(new NotifyAiCredits(message.OrganizationId, CreditAlert.ReloadFailed, ReloadFailedKey(purchase.Id), purchase.Amount));
            outgoing.Add(new BillingChanged(message.OrganizationId));
            LogDeclined(logger, message.OrganizationId, purchase.Id, charge.FailureCode);
            return outgoing;
        }

        purchase.RecordProviderPayment(charge.PaymentId ?? $"reload-{purchase.Id:N}");
        await db.SaveChangesAsync(cancellationToken);
        // Stripe confirms the payment with a signed invoice.paid, which grants. The sandbox has no webhook, so it grants here.
        if (billing.Mode == BillingMode.Sandbox && charge.Status == CreditChargeStatus.Succeeded)
        {
            outgoing.Add(new GrantAiCredits(message.OrganizationId, purchase.Id, purchase.ProviderPaymentId!));
        }

        LogCharged(logger, message.OrganizationId, purchase.Id, purchase.Amount);
        return outgoing;
    }

    private static string ReloadFailedKey(Guid purchaseId) => $"reload-failed:{purchaseId:N}";

    [LoggerMessage(Level = LogLevel.Information, Message = "Organization {OrganizationId}: auto-reload did not run: {Outcome}")]
    private static partial void LogNotReloaded(ILogger logger, Guid organizationId, AutoReloadOutcome outcome);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Organization {OrganizationId}: auto-reload {PurchaseId} was refused ({Code}); auto-reload is off and the Owners are told")]
    private static partial void LogDeclined(ILogger logger, Guid organizationId, Guid purchaseId, string? code);

    [LoggerMessage(Level = LogLevel.Information, Message = "Organization {OrganizationId}: auto-reload {PurchaseId} charged {Amount} US dollars")]
    private static partial void LogCharged(ILogger logger, Guid organizationId, Guid purchaseId, decimal amount);
}

/// <summary>
/// Tells the Owners about their credits: a notification in the bell, and an email by each Owner's own choice for AI use and billing on
/// Your account. The organization's budget alert defaults are not read: an organization that turned those off must still hear that a
/// charge failed. The key is claimed first, so the message is sent once however often it arrives.
/// </summary>
public static partial class NotifyAiCreditsHandler
{
    public static async Task<OutgoingMessages> Handle(
        NotifyAiCredits message,
        ScreenHireDbContext db,
        TransactionalEmail email,
        IOptions<DashboardLinkOptions> links,
        TimeProvider time,
        ILogger<NotifyAiCredits> logger,
        CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();
        var now = time.GetUtcNow();
        var claimed = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO ai_usage_alerts (id, organization_id, key, raised_at)
            VALUES ({Guid.CreateVersion7()}, {message.OrganizationId}, {message.Key}, {now})
            ON CONFLICT (organization_id, key) DO NOTHING
            """,
            cancellationToken);
        if (claimed == 0)
        {
            return outgoing;
        }

        var amount = message.Amount.ToString("0.00", CultureInfo.InvariantCulture);
        var (kind, payload) = message.Kind switch
        {
            CreditAlert.Low => (NotificationKinds.AiCreditsLow, new Dictionary<string, string> { ["balance"] = amount }),
            CreditAlert.Empty => (NotificationKinds.AiCreditsEmpty, new Dictionary<string, string>()),
            _ => (NotificationKinds.AiCreditsReloadFailed, new Dictionary<string, string> { ["amount"] = amount }),
        };
        if (message.Kind == CreditAlert.Empty)
        {
            // Run out is the more urgent line; the low one has said its piece.
            outgoing.Add(new ClearNotification(message.OrganizationId, NotificationKinds.AiCreditsLow, NotificationKinds.CreditsKey));
        }

        outgoing.Add(new RaiseNotification(message.OrganizationId, Permission.ManageBilling, kind, NotificationKinds.CreditsKey, payload, AiCreditWording.Path));

        var link = links.Value.Url(AiCreditWording.Path);
        var organization = await db.Organizations.AsNoTracking().Where(o => o.Id == message.OrganizationId).Select(o => o.Name).SingleOrDefaultAsync(cancellationToken);
        if (link is null || organization is null)
        {
            return outgoing;
        }

        var owners = await NotificationRecipients.ForAsync(db, message.OrganizationId, Permission.ManageBilling, cancellationToken);
        var preferences = await PersonNotices.ForUsersAsync(db, message.OrganizationId, [.. owners.Select(o => o.UserId)], cancellationToken);
        foreach (var owner in owners.DistinctBy(o => o.UserId))
        {
            var choice = PersonNotices.Email(preferences.GetValueOrDefault(owner.UserId), NoticeKind.AiUseAndBilling, OrganizationAlertDefaults.None, handlesBilling: true);
            var notice = new Notice(message.OrganizationId, owner.UserId, NoticeKind.AiUseAndBilling, message.Key, Detail: AiCreditWording.Detail);
            await NoticeDelivery.EmailAsync(
                db, email, links.Value, notice, choice,
                () => AiCreditWording.Email(owner.Email, message.Kind, message.Amount, organization, link), now, cancellationToken);
        }

        LogNotified(logger, message.OrganizationId, message.Kind, owners.Count);
        return outgoing;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Organization {OrganizationId}: credit alert {Kind}; {Owners} Owners could be told")]
    private static partial void LogNotified(ILogger logger, Guid organizationId, CreditAlert kind, int owners);
}

/// <summary>The words of the credit alerts, for the email and the digest. Fixed text and amounts only.</summary>
public static class AiCreditWording
{
    /// <summary>Where every credit alert links: the credits panel on Billing.</summary>
    public const string Path = "/settings/billing#ai-credits";

    /// <summary>The digest's <see cref="Notice.Detail"/> for a credit alert.</summary>
    public const string Detail = "Credits";

    public static string Subject(CreditAlert kind) => kind switch
    {
        CreditAlert.Low => "AI credits are running low",
        CreditAlert.Empty => "AI credits ran out: the AI checks are paused",
        _ => "Auto-reload could not add AI credits",
    };

    public static string Body(CreditAlert kind, decimal amount, string organization)
    {
        var dollars = amount.ToString("C2", CultureInfo.GetCultureInfo("en-US"));
        return kind switch
        {
            CreditAlert.Low =>
                $"{organization} has {dollars} of AI credits left. When they run out, the AI checks pause until credits are added. Add credits or turn on auto-reload in Billing.",
            CreditAlert.Empty =>
                $"{organization} has no AI credits left, so the AI checks are paused until credits are added. The other checks keep running, and a paused check is never a mark against a candidate.",
            _ =>
                $"The card on file was not charged {dollars} for {organization}'s AI credits, so auto-reload is now off. Buy credits in Billing to save a card that works, then turn auto-reload on again.",
        };
    }

    public static EmailMessage Email(string to, CreditAlert kind, decimal amount, string organization, Uri link)
    {
        var body = Body(kind, amount, organization);
        return new EmailMessage(
            to, Subject(kind),
            $"{body}\n\nOpen Billing: {link}",
            $"<p>{WebUtility.HtmlEncode(body)}</p><p><a href=\"{WebUtility.HtmlEncode(link.ToString())}\">Open Billing</a>.</p>");
    }
}

public static class ExpireAiCreditsHandler
{
    // Like the other fan-outs: row-level security gives the worker no read across organizations, so it asks each one in turn.
    public static async Task<OutgoingMessages> Handle(ExpireAiCredits message, ScreenHireDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var day = AiUsageQueries.Day(time.GetUtcNow());
        var organizations = await db.Organizations.AsNoTracking().Select(o => o.Id).ToListAsync(cancellationToken);
        var outgoing = new OutgoingMessages();
        outgoing.AddRange(organizations.Select(id => new ExpireOrganizationAiCredits(id, day)));
        return outgoing;
    }
}

/// <summary>
/// Writes off what is left of each grant past its expiry (<see cref="CreditLots"/>): one <c>Expiry</c> entry per grant, keyed on it, so
/// a second run the same day, or a later one, writes nothing again.
/// </summary>
public static partial class ExpireOrganizationAiCreditsHandler
{
    [Deduplicated(ValueSource.InputMember, nameof(ExpireOrganizationAiCredits.IdempotencyKey))]
    public static async Task<OutgoingMessages> Handle(
        ExpireOrganizationAiCredits message,
        ScreenHireDbContext db,
        IOptions<BillingOptions> options,
        TimeProvider time,
        ILogger<ExpireOrganizationAiCredits> logger,
        CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();
        var now = time.GetUtcNow();
        if (!options.Value.CreditsEnabled || !await db.CreditEntries.AsNoTracking().AnyAsync(e => e.ExpiresAt != null && e.ExpiresAt <= now, cancellationToken))
        {
            return outgoing;
        }

        var ledger = await db.CreditEntries.AsNoTracking()
            .Select(e => new CreditMovement(e.Id, e.At, e.Micros, e.ExpiresAt, e.Kind, e.GrantEntryId))
            .ToListAsync(cancellationToken);
        var expiring = CreditLots.Expiring(ledger, now);
        if (expiring.Count == 0)
        {
            return outgoing;
        }

        db.CreditEntries.AddRange(expiring.Select(e => new CreditEntry
        {
            OrganizationId = message.OrganizationId,
            Kind = CreditEntryKind.Expiry,
            Micros = -e.Micros,
            At = now,
            SourceKey = CreditSourceKeys.Expiry(e.GrantId),
            GrantEntryId = e.GrantId,
        }));
        await db.SaveChangesAsync(cancellationToken);
        outgoing.Add(new BillingChanged(message.OrganizationId));
        LogExpired(logger, message.OrganizationId, expiring.Count, AiCharge.Dollars(expiring.Sum(e => e.Micros)));
        return outgoing;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Organization {OrganizationId}: {Grants} credit grants expired, {Amount} US dollars written off")]
    private static partial void LogExpired(ILogger logger, Guid organizationId, int grants, decimal amount);
}

public static class AiCreditRegistration
{
    /// <summary>
    /// What the worker needs for AI credits: the billing settings and their checks, the plan catalogue and both payment providers behind
    /// <see cref="BillingAccess"/>, to grant, reload and expire credits. Registered by the worker's messaging setup.
    /// </summary>
    public static IServiceCollection AddScreenHireAiCredits(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<BillingOptions>().BindConfiguration(BillingOptions.Section).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<BillingOptions>, BillingSandboxGate>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<BillingOptions>, BillingAiGate>());
        services.TryAddSingleton<PlanCatalogue>();
        services.TryAddSingleton<StripeBillingProvider>();
        services.TryAddSingleton<SandboxBillingProvider>();
        services.TryAddScoped<BillingAccess>();
        return services;
    }
}
