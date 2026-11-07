using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ScreenHire.Domain.Billing;
using ScreenHire.Domain.Jobs;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Application.Billing;

/// <summary>
/// Counts the jobs open across a whole organization. The caller's own scope may be narrower: a member on "only jobs they're
/// on" sees only those jobs (the <c>job_scope</c> filter and policy), and a plan limit counts every job. So the count runs
/// in a scope of its own that acts for the organization, as a worker handler or a careers page does.
/// </summary>
public sealed class OpenJobCounter(IServiceScopeFactory scopes)
{
    // `excluding` is a job not to count: the draft being opened, which a double submit may already have opened.
    public async Task<int> CountAsync(Guid organizationId, Guid? excluding, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetPublicOrganization(organizationId);
        var db = scope.ServiceProvider.GetRequiredService<ScreenHireDbContext>();
        return await db.Jobs.CountAsync(j => j.Status == JobStatus.Open && j.Id != excluding, cancellationToken);
    }
}

/// <summary>What the Billing page shows, before it is mapped to the API's contract.</summary>
public sealed record BillingStatus(
    BillingState State,
    Plan? Plan,
    DateTimeOffset? TrialEndsAt,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelsAtPeriodEnd,
    bool HasCustomer,
    int OpenJobs,
    int? Limit);

/// <summary>
/// Billing's rules in one place (ADR-0029): the account every organization has while billing is on, what it may publish,
/// and applying what the provider reports. The domain's <see cref="Entitlement"/> decides; this loads and saves.
/// </summary>
public sealed class BillingService(
    ScreenHireDbContext db, BillingAccess access, PlanCatalogue catalogue, OpenJobCounter counter, IOptions<BillingOptions> options, TimeProvider time)
{
    /// <summary>Whether billing is on here. Off: no account, no trial, no limits.</summary>
    public bool IsOn => access.IsConfigured;

    /// <summary>A trial account for a new organization, not yet saved. Null while billing is off.</summary>
    public BillingAccount? NewTrial(Guid organizationId) =>
        IsOn ? BillingAccount.StartTrial(organizationId, time.GetUtcNow(), options.Value.Trial.Days) : null;

    /// <summary>
    /// The organization's account, starting its trial now when it has none (an organization older than billing). Null while
    /// billing is off. The tenant must be the organization.
    /// </summary>
    public async Task<BillingAccount?> AccountAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        if (!IsOn)
        {
            return null;
        }

        var existing = await db.BillingAccounts.SingleOrDefaultAsync(a => a.OrganizationId == organizationId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var account = NewTrial(organizationId)!;
        db.BillingAccounts.Add(account);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return account;
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            // Another request started it first.
            db.Entry(account).State = EntityState.Detached;
            return await db.BillingAccounts.SingleAsync(a => a.OrganizationId == organizationId, cancellationToken);
        }
    }

    public async Task<BillingStatus?> StatusAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var account = await AccountAsync(organizationId, cancellationToken);
        if (account is null)
        {
            return null;
        }

        var now = time.GetUtcNow();
        var state = Entitlement.StateOf(account, now);
        var plan = catalogue.Find(Entitlement.EffectivePlanKey(account, now, options.Value.Trial.Plan));
        var onTrial = state == BillingState.Trialing;
        return new BillingStatus(
            state,
            plan,
            onTrial ? account.TrialEndsAt : null,
            account.CurrentPeriodEnd,
            account.CancelsAtPeriodEnd,
            !string.IsNullOrEmpty(account.ProviderCustomerId),
            await counter.CountAsync(organizationId, null, cancellationToken),
            plan?.Limit);
    }

    /// <summary>
    /// Whether the organization may open one more job (<paramref name="jobId"/>, still a draft as far as the caller knows; it is
    /// not counted, so a double submit that already opened it is not refused for taking its own slot). Allowed while billing is
    /// off, and when the plan has no limit. Call it
    /// inside the publish transaction: the per-organization lock it takes lasts until that commits, so two jobs opening at
    /// once cannot both slip under the limit.
    /// </summary>
    public async Task<PublishDecision> CheckPublishAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken)
    {
        if (await AccountAsync(organizationId, cancellationToken) is not { } account)
        {
            return new PublishDecision(PublishDecisionKind.Allowed);
        }

        var now = time.GetUtcNow();
        var limit = catalogue.Find(Entitlement.EffectivePlanKey(account, now, options.Value.Trial.Plan))?.Limit;
        var openJobs = 0;
        if (limit is not null)
        {
            await db.Database.ExecuteSqlAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({"billing-publish:" + organizationId.ToString("N")}, 0))", cancellationToken);
            openJobs = await counter.CountAsync(organizationId, jobId, cancellationToken);
        }

        return Entitlement.CanPublishDraft(account, now, limit, openJobs);
    }

    /// <summary>
    /// Records <paramref name="providerEvent"/> and applies it to the organization's account, in the caller's save. False
    /// when it was applied before, is about another organization than the tenant, is older than what the account knows, or
    /// ends a subscription other than the one the account has now (an earlier one the owner replaced).
    /// </summary>
    public async Task<bool> ApplyAsync(BillingProviderEvent providerEvent, Guid organizationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(providerEvent);
        if (providerEvent.OrganizationId != organizationId)
        {
            return false;
        }

        if (await db.BillingEvents.AnyAsync(e => e.EventId == providerEvent.EventId, cancellationToken))
        {
            return false;
        }

        var account = await AccountAsync(organizationId, cancellationToken);
        if (account is null)
        {
            return false;
        }

        var now = time.GetUtcNow();
        db.BillingEvents.Add(new BillingEventRecord { OrganizationId = organizationId, EventId = providerEvent.EventId, ReceivedAt = now });
        if (providerEvent is { Status: SubscriptionStatus.Ended, SubscriptionId: { } ended }
            && account.ProviderSubscriptionId is { } current && ended != current)
        {
            return false; // recorded, so a redelivery is a no-op, but the subscription that pays is not the one that ended
        }

        var plan = catalogue.Find(providerEvent.PlanKey);
        return account.Apply(
            providerEvent.CreatedAt, providerEvent.CustomerId, providerEvent.SubscriptionId, plan?.Key, providerEvent.Status,
            providerEvent.CurrentPeriodEnd, providerEvent.CancelsAtPeriodEnd, now);
    }
}
