using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Application.Billing;
using ScreenHire.Application.Notifications;
using ScreenHire.Domain.Billing;
using ScreenHire.Domain.Notifications;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// Stripe's signed webhook (docs/architecture/billing.md, ADR-0029): an anonymous route with its own check, the
/// <c>Stripe-Signature</c> header, and not in the generated client. A signed event names its organization in the metadata
/// the Checkout carried; the signature is what makes that name trustworthy. A signed delivery gets a 200, even one for an
/// event type or an organization this app does not know, so Stripe stops retrying it. The one exception is a delivery that
/// lost a race for its account, which gets a 409 so Stripe sends it again.
/// </summary>
internal static class BillingWebhookEndpoints
{
    public const string RateLimitPolicy = "billing-webhook";
    public const string StripePath = "/v1/webhooks/stripe";

    private const int MaxBodyBytes = 1024 * 1024;

    public static IServiceCollection AddScreenHireBillingWebhooks(this IServiceCollection services)
    {
        // Per client address. The limiter runs before the handler checks the signature, so one shared limit would let a
        // flood of unsigned posts use up the permits Stripe's real deliveries need.
        services.Configure<RateLimiterOptions>(options => options.AddPolicy(RateLimitPolicy, context =>
            RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 600,
                Window = TimeSpan.FromMinutes(1),
            })));
        return services;
    }

    public static IEndpointRouteBuilder MapBillingWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(StripePath, ReceiveStripeEvent)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicy)
            .ExcludeFromDescription()
            .WithMetadata(new RequestSizeLimitAttribute(MaxBodyBytes));
        return app;
    }

    private static async Task<IResult> ReceiveStripeEvent(
        HttpRequest request,
        BillingAccess access,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        TenantContext tenant,
        BillingService billing,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (access.Mode != BillingMode.Stripe)
        {
            return Results.NotFound(); // not set up here
        }

        string body;
        using (var reader = new StreamReader(request.Body))
        {
            body = await reader.ReadToEndAsync(cancellationToken);
        }

        var webhook = access.Provider.ReadWebhook(body, request.Headers[StripeSignature.Header].ToString(), time.GetUtcNow());
        if (!webhook.SignatureValid)
        {
            return Results.Unauthorized();
        }

        // A credit purchase confirmed as paid (ADR-0032): the worker grants it.
        if (webhook.Credit is { } credit)
        {
            return await CreditPaymentApplier.ApplyAsync(outbox, tenant, credit, time.GetUtcNow(), cancellationToken) == BillingApply.Conflict
                ? Results.Conflict()
                : Results.Ok();
        }

        // A conflict is a race with another delivery for the same account: answer 409 so Stripe sends this one again.
        return webhook.Event is { } providerEvent
            && await BillingEventApplier.ApplyAsync(outbox, tenant, billing, providerEvent, cancellationToken) == BillingApply.Conflict
            ? Results.Conflict()
            : Results.Ok();
    }
}

/// <summary>
/// Applies one provider event for the organization it names, from a route that has no signed-in user (the Stripe webhook,
/// the sandbox's pages). The tenant is that organization, as every anonymous route that acts for one does
/// (<c>TenantContext.SetPublicOrganization</c>).
/// </summary>
internal static class BillingEventApplier
{
    /// <summary>Applied; Ignored for a repeat, a stale event or an organization that does not exist; Conflict when another delivery changed the account first.</summary>
    public static async Task<BillingApply> ApplyAsync(
        IDbContextOutbox<ScreenHireDbContext> outbox, TenantContext tenant, BillingService billing, BillingProviderEvent providerEvent, CancellationToken cancellationToken)
    {
        if (providerEvent.OrganizationId is not { } organizationId)
        {
            return BillingApply.Ignored;
        }

        tenant.SetPublicOrganization(organizationId);
        var db = outbox.DbContext;
        if (!await db.Organizations.AsNoTracking().AnyAsync(o => o.Id == organizationId, cancellationToken))
        {
            return BillingApply.Ignored;
        }

        try
        {
            var changed = await billing.ApplyAsync(providerEvent, organizationId, cancellationToken);
            if (changed)
            {
                await outbox.PublishAsync(new BillingChanged(organizationId));

                // Once a subscription has started, the trial reminders in the Owners' bell stop being true (the scheduled ones
                // check the account themselves and do nothing). The account is tracked, so this is the state about to be saved.
                if (db.BillingAccounts.Local.Any(a => a.Status != SubscriptionStatus.None))
                {
                    await outbox.PublishAsync(new ClearNotification(organizationId, NotificationKinds.TrialEnding, NotificationKinds.TrialKey));
                    await outbox.PublishAsync(new ClearNotification(organizationId, NotificationKinds.TrialEnded, NotificationKinds.TrialKey));
                }
            }

            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
            return changed ? BillingApply.Applied : BillingApply.Ignored;
        }
        catch (DbUpdateConcurrencyException)
        {
            return BillingApply.Conflict;
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            // The same event delivered twice at once: the other delivery applied it.
            return BillingApply.Ignored;
        }
    }
}

/// <summary>
/// Turns a confirmed credit payment into <see cref="GrantAiCredits"/> for the worker, from a route with no signed-in user (the Stripe
/// webhook, the sandbox's credit Checkout). The event id is recorded in <c>billing_events</c> in the same save, so a redelivery publishes
/// nothing, and the worker's handler is deduplicated on the purchase besides. The customer the payment made is kept on the account when it
/// has none, without counting as a subscription event: <see cref="BillingAccount.AdoptCustomer"/> leaves <c>LastEventAt</c> alone.
/// </summary>
internal static class CreditPaymentApplier
{
    public static async Task<BillingApply> ApplyAsync(
        IDbContextOutbox<ScreenHireDbContext> outbox, TenantContext tenant, CreditPaymentEvent payment, DateTimeOffset now, CancellationToken cancellationToken)
    {
        tenant.SetPublicOrganization(payment.OrganizationId);
        var db = outbox.DbContext;
        if (!await db.Organizations.AsNoTracking().AnyAsync(o => o.Id == payment.OrganizationId, cancellationToken)
            || await db.BillingEvents.AsNoTracking().AnyAsync(e => e.EventId == payment.EventId, cancellationToken))
        {
            return BillingApply.Ignored;
        }

        db.BillingEvents.Add(new BillingEventRecord { OrganizationId = payment.OrganizationId, EventId = payment.EventId, ReceivedAt = now });
        if (payment.CustomerId is { } customer && await db.BillingAccounts.SingleOrDefaultAsync(cancellationToken) is { } account)
        {
            account.AdoptCustomer(customer, now);
        }

        await outbox.PublishAsync(new GrantAiCredits(payment.OrganizationId, payment.PurchaseId, payment.ProviderPaymentId, payment.PaymentIntentId));
        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
            return BillingApply.Applied;
        }
        catch (DbUpdateConcurrencyException)
        {
            return BillingApply.Conflict;
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            // The same event delivered twice at once: the other delivery published the grant.
            return BillingApply.Ignored;
        }
    }
}

internal enum BillingApply

{
    Applied,
    Ignored,
    Conflict,
}
