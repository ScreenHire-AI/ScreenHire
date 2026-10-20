using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using ScreenHire.Api.Applications;
using ScreenHire.Api.Configuration;
using ScreenHire.Api.Contracts;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// Telemetry ingest for the careers apply form (integrity-engine.md §10.2): the page's module sends cumulative
/// snapshots of session aggregates with <c>navigator.sendBeacon</c> to the careers server, which forwards them here.
/// Anonymous, like the rest of the apply flow, and bound to the session token the application form carried.
/// </summary>
/// <remarks>
/// Strict on purpose: unknown members, unknown field ids, unknown automation markers and bodies over
/// <see cref="MaxBodyBytes"/> are refused, so nothing but counts can arrive. A snapshot never refuses or changes an
/// application, and its own rate limit keeps beacons from using up the candidate's apply allowance.
/// </remarks>
internal static class PublicTelemetryEndpoints
{
    /// <summary>The largest snapshot body taken. A full form's snapshot is a few kilobytes.</summary>
    public const int MaxBodyBytes = 32 * 1024;

    public static IEndpointRouteBuilder MapPublicTelemetryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/public/organizations/{organizationSlug}/jobs/{jobId:guid}/telemetry", StoreSnapshot)
            .WithTags("Public careers")
            .WithName("SendPublicTelemetry")
            .AllowAnonymous()
            .RequireRateLimiting(ApplyOptions.TelemetryRateLimitPolicy)
            .WithMetadata(new RequestSizeLimitAttribute(MaxBodyBytes))
            .ProducesProblem(StatusCodes.Status410Gone)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .Produces(StatusCodes.Status429TooManyRequests);

        return app;
    }

    private static async Task<Results<Accepted, NotFound, ValidationProblem, ProblemHttpResult>> StoreSnapshot(
        string organizationSlug,
        Guid jobId,
        TelemetrySnapshotInput snapshot,
        IValidator<TelemetrySnapshotInput> validator,
        TelemetrySessions sessions,
        ScreenHireDbContext db,
        TenantContext tenant,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (await validator.ProblemsAsync(snapshot, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var (job, gone) = await PublicApplicationEndpoints.FindOpenJobAsync(organizationSlug, jobId, db, tenant, cancellationToken);
        if (gone is not null)
        {
            return gone;
        }

        if (job is null)
        {
            return TypedResults.NotFound();
        }

        if (sessions.Read(snapshot.SessionToken, job.OrganizationId, job.Id) is not { } sessionId)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("sessionToken", "This form session has ended. Reload the form."));
        }

        var known = TelemetrySessions.FieldIdsFor(job);
        if (snapshot.Fields.Any(f => !known.Contains(f.FieldId)))
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("fields", "The form has no field with this id."));
        }

        await TelemetrySessions.StoreAsync(db, job, sessionId, snapshot, time.GetUtcNow(), cancellationToken);
        return TypedResults.Accepted((string?)null);
    }
}
