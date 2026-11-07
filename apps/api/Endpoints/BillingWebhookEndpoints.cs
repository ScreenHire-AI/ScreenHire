using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Application.Billing;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// Stripe's signed webhook (docs/architecture/billing.md, ADR-0029): an anonymous route with its own check, the
/// <c>Stripe-Signature</c> header, and not in the generated client. A signed event names its organization in the metadata
/// the Checkout carried; the signature is what makes that name trustworthy. Every delivery that is signed gets a 200, even
/// one for an event type or an organization this app does not know, so Stripe stops retrying it.
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

        if (webhook.Event is { } providerEvent)
        {
            await BillingEventApplier.ApplyAsync(outbox, tenant, billing, providerEvent, cancellationToken);
        }

        return Results.Ok();
    }
}

/// <summary>
/// Applies one provider event for the organization it names, from a route that has no signed-in user (the Stripe webhook,
/// the sandbox's pages). The tenant is that organization, as every anonymous route that acts for one does
/// (<c>TenantContext.SetPublicOrganization</c>).
/// </summary>
internal static class BillingEventApplier
{
    /// <summary>True when the event changed the account. False for a repeat, a stale event, or an organization that does not exist.</summary>
    public static async Task<bool> ApplyAsync(
        IDbContextOutbox<ScreenHireDbContext> outbox, TenantContext tenant, BillingService billing, BillingProviderEvent providerEvent, CancellationToken cancellationToken)
    {
        if (providerEvent.OrganizationId is not { } organizationId)
        {
            return false;
        }

        tenant.SetPublicOrganization(organizationId);
        var db = outbox.DbContext;
        if (!await db.Organizations.AsNoTracking().AnyAsync(o => o.Id == organizationId, cancellationToken))
        {
            return false;
        }

        try
        {
            var changed = await billing.ApplyAsync(providerEvent, organizationId, cancellationToken);
            if (changed)
            {
                await outbox.PublishAsync(new BillingChanged(organizationId));
            }

            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
            return changed;
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            // The same event delivered twice at once: the other delivery applied it.
            return false;
        }
    }
}
