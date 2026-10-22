using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.Automations;
using ScreenHire.Application.Webhooks;
using ScreenHire.Domain.Webhooks;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// An organization's webhook subscriptions (docs/architecture/workflow-engine.md §5): endpoints, their signing secrets,
/// the delivery log and replay. Owners and admins only, for reading too: an endpoint's URL and its log describe the
/// organization's integrations. Another organization's endpoint or delivery is a 404.
/// </summary>
internal static class WebhookEndpoints
{
    /// <summary>Endpoints per organization: enough for every integration, few enough that one event never fans out far.</summary>
    public const int MaxEndpoints = 20;

    private const int DefaultDeliveryLimit = 50;

    private const int MaxDeliveryLimit = 200;

    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/webhooks").WithTags("Webhooks").RequireAuthorization();
        group.MapGet("/event-types", ListWebhookEventTypes).WithName("ListWebhookEventTypes").Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("", ListWebhookEndpoints).WithName("ListWebhookEndpoints")
            .RequireOrganizationAdmin()
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("", CreateWebhookEndpoint).WithName("CreateWebhookEndpoint")
            .RequireAntiforgeryToken()
            .RequireOrganizationAdmin()
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/{endpointId:guid}", GetWebhookEndpoint).WithName("GetWebhookEndpoint")
            .RequireOrganizationAdmin()
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPut("/{endpointId:guid}", UpdateWebhookEndpoint).WithName("UpdateWebhookEndpoint")
            .RequireAntiforgeryToken()
            .RequireOrganizationAdmin()
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapDelete("/{endpointId:guid}", DeleteWebhookEndpoint).WithName("DeleteWebhookEndpoint")
            .RequireAntiforgeryToken()
            .RequireOrganizationAdmin()
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/{endpointId:guid}/rotate-secret", RotateWebhookSecret).WithName("RotateWebhookSecret")
            .RequireAntiforgeryToken()
            .RequireOrganizationAdmin()
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/{endpointId:guid}/deliveries", ListWebhookDeliveries).WithName("ListWebhookDeliveries")
            .RequireOrganizationAdmin()
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/{endpointId:guid}/deliveries/{deliveryId:guid}/replay", ReplayWebhookDelivery).WithName("ReplayWebhookDelivery")
            .RequireAntiforgeryToken()
            .RequireOrganizationAdmin()
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
    }

    private static Ok<IReadOnlyList<string>> ListWebhookEventTypes() => TypedResults.Ok(WebhookEventTypes.All);

    private static async Task<Ok<List<WebhookEndpointEntry>>> ListWebhookEndpoints(ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var endpoints = await db.WebhookEndpoints.AsNoTracking().OrderBy(e => e.CreatedAt).ThenBy(e => e.Id).ToListAsync(cancellationToken);
        return TypedResults.Ok(endpoints.Select(e => e.ToEntry()).ToList());
    }

    private static async Task<Results<Ok<WebhookEndpointEntry>, NotFound>> GetWebhookEndpoint(
        Guid endpointId, ScreenHireDbContext db, CancellationToken cancellationToken) =>
        await db.WebhookEndpoints.AsNoTracking().SingleOrDefaultAsync(e => e.Id == endpointId, cancellationToken) is { } endpoint
            ? TypedResults.Ok(endpoint.ToEntry())
            : TypedResults.NotFound();

    private static async Task<Results<Created<WebhookEndpointWithSecret>, ValidationProblem, ProblemHttpResult>> CreateWebhookEndpoint(
        CreateWebhookEndpointRequest request,
        ScreenHireDbContext db,
        ITenantContext tenant,
        IOptions<WebhookOptions> options,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (WebhookValidation.Problems(request.Url, request.Events, request.Description, options.Value.AllowLoopback) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        if (string.IsNullOrEmpty(options.Value.SigningKey))
        {
            return NotSetUp();
        }

        if (await db.WebhookEndpoints.CountAsync(cancellationToken) >= MaxEndpoints)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem(
                "url", $"An organization has at most {MaxEndpoints} webhook endpoints. Delete one first."));
        }

        var endpoint = WebhookEndpoint.Create(tenant.OrganizationId!.Value, request.Url, Blank(request.Description), request.Events, time.GetUtcNow());
        db.WebhookEndpoints.Add(endpoint);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/v1/webhooks/{endpoint.Id}", WithSecret(endpoint, options.Value.SigningKey));
    }

    private static async Task<Results<Ok<WebhookEndpointEntry>, NotFound, ValidationProblem>> UpdateWebhookEndpoint(
        Guid endpointId,
        UpdateWebhookEndpointRequest request,
        ScreenHireDbContext db,
        IOptions<WebhookOptions> options,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var endpoint = await db.WebhookEndpoints.SingleOrDefaultAsync(e => e.Id == endpointId, cancellationToken);
        if (endpoint is null)
        {
            return TypedResults.NotFound();
        }

        if (WebhookValidation.Problems(request.Url, request.Events, request.Description, options.Value.AllowLoopback) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var now = time.GetUtcNow();
        endpoint.Url = request.Url;
        endpoint.Description = Blank(request.Description);
        endpoint.Events = [.. request.Events.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        endpoint.UpdatedAt = now;
        endpoint.SetEnabled(request.Enabled, now);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(endpoint.ToEntry());
    }

    // Its delivery log goes with it (cascade). Attempts already scheduled find no delivery and send nothing.
    private static async Task<Results<NoContent, NotFound>> DeleteWebhookEndpoint(
        Guid endpointId, ScreenHireDbContext db, CancellationToken cancellationToken) =>
        await db.WebhookEndpoints.Where(e => e.Id == endpointId).ExecuteDeleteAsync(cancellationToken) == 0
            ? TypedResults.NotFound()
            : TypedResults.NoContent();

    private static async Task<Results<Ok<WebhookEndpointWithSecret>, NotFound, ProblemHttpResult>> RotateWebhookSecret(
        Guid endpointId, ScreenHireDbContext db, IOptions<WebhookOptions> options, TimeProvider time, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(options.Value.SigningKey))
        {
            return NotSetUp();
        }

        var endpoint = await db.WebhookEndpoints.SingleOrDefaultAsync(e => e.Id == endpointId, cancellationToken);
        if (endpoint is null)
        {
            return TypedResults.NotFound();
        }

        endpoint.RotateSecret(time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(WithSecret(endpoint, options.Value.SigningKey));
    }

    // Newest first.
    private static async Task<Results<Ok<List<WebhookDeliveryEntry>>, NotFound, ValidationProblem>> ListWebhookDeliveries(
        Guid endpointId, int? limit, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > MaxDeliveryLimit)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("limit", $"A limit is between 1 and {MaxDeliveryLimit}."));
        }

        if (!await db.WebhookEndpoints.AnyAsync(e => e.Id == endpointId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var deliveries = await db.WebhookDeliveries.AsNoTracking()
            .Where(d => d.EndpointId == endpointId)
            .OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.Id)
            .Take(limit ?? DefaultDeliveryLimit)
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(deliveries.Select(d => d.ToEntry()).ToList());
    }

    // A new delivery of the same event and body, signed with the endpoint's current secret. Receivers that deduplicate on
    // the event id (the Idempotency-Key header) see the same id again, which is the point: a replay is the same event.
    private static async Task<Results<Accepted<WebhookDeliveryEntry>, NotFound, ProblemHttpResult>> ReplayWebhookDelivery(
        Guid endpointId, Guid deliveryId, IDbContextOutbox<ScreenHireDbContext> outbox, TimeProvider time, CancellationToken cancellationToken)
    {
        var db = outbox.DbContext;
        var endpoint = await db.WebhookEndpoints.AsNoTracking().SingleOrDefaultAsync(e => e.Id == endpointId, cancellationToken);
        var original = await db.WebhookDeliveries.AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == deliveryId && d.EndpointId == endpointId, cancellationToken);
        if (endpoint is null || original is null)
        {
            return TypedResults.NotFound();
        }

        if (!endpoint.Enabled)
        {
            return TypedResults.Problem(
                title: "Endpoint disabled", detail: "Turn the endpoint back on before replaying to it.", statusCode: StatusCodes.Status409Conflict);
        }

        if (original.Status == WebhookDeliveryStatus.Pending)
        {
            return TypedResults.Problem(
                title: "Still delivering", detail: "This delivery has attempts left. Replay it once it has finished.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var replay = original.Replay(time.GetUtcNow());
        db.WebhookDeliveries.Add(replay);
        await outbox.PublishAsync(new DeliverWebhook(replay.OrganizationId, replay.Id, 1));
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        return TypedResults.Accepted($"/v1/webhooks/{endpointId}/deliveries", replay.ToEntry());
    }

    private static WebhookEndpointWithSecret WithSecret(WebhookEndpoint endpoint, string signingKey) =>
        new(endpoint.ToEntry(), WebhookSignature.EndpointSecret(signingKey, endpoint.Id, endpoint.SecretVersion));

    private static ProblemHttpResult NotSetUp() => TypedResults.Problem(
        title: "Webhooks are not set up",
        detail: "This deployment has no webhook signing key (ScreenHire:Webhooks:SigningKey), so it cannot sign deliveries.",
        statusCode: StatusCodes.Status503ServiceUnavailable);

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
