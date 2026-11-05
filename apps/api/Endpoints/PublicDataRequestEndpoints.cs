using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Applications;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.Privacy;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Organizations;
using ScreenHire.Domain.Privacy;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// The candidate's side of data requests (docs/architecture/data-requests.md): ask for a link by email address, open the
/// choices with the emailed link, ask for a copy of their data or for its erasure, and download the copy. Anonymous, rate
/// limited per address like the apply flow, and called by the careers server.
/// </summary>
/// <remarks>
/// <para>
/// Asking for a link answers 202 with no body whether or not the address has applied, so it tells nobody who has. The
/// work is the same either way except for the claim and the message; the remaining difference in timing is an accepted limit.
/// </para>
/// <para>
/// Every route that takes a token gives a 404 for a token that is forged, damaged, expired, made for another organization
/// or for a candidate who has since been erased, so a page cannot tell them apart. The routes are also a 404 where the
/// deployment cannot send the email (no sender, no careers address), because there is nothing for them to belong to.
/// </para>
/// </remarks>
internal static class PublicDataRequestEndpoints
{
    /// <summary>The name the browser saves the copy under.</summary>
    internal const string DownloadFileName = "your-data.json";

    public static IEndpointRouteBuilder MapPublicDataRequestEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/public/organizations/{organizationSlug}/data-requests")
            .WithTags("Public careers")
            .AllowAnonymous()
            .RequireRateLimiting(DataRequestOptions.RateLimitPolicy);
        group.MapGet("", GetForm).WithName("GetPublicDataRequestForm")
            .Produces(StatusCodes.Status429TooManyRequests);
        group.MapPost("links", RequestLink).WithName("RequestPublicDataRequestLink")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status429TooManyRequests);
        group.MapGet("{token}", GetChoices).WithName("GetPublicDataRequestChoices")
            .Produces(StatusCodes.Status429TooManyRequests);
        group.MapPost("{token}", MakeRequest).WithName("MakePublicDataRequest")
            .Produces(StatusCodes.Status429TooManyRequests);
        group.MapGet("downloads/{token}", GetDownload).WithName("GetPublicDataDownload")
            .Produces(StatusCodes.Status429TooManyRequests);
        return app;
    }

    private static async Task<Results<Ok<PublicDataRequestForm>, NotFound>> GetForm(
        string organizationSlug,
        ScreenHireDbContext db,
        TenantContext tenant,
        DataRequestLinks links,
        IServiceProvider services,
        TurnstileVerifier turnstile,
        CancellationToken cancellationToken)
    {
        if (await PublicOrganizations.FindAsync(organizationSlug, db, tenant, cancellationToken) is not { } organization)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(new PublicDataRequestForm(organization.Name, IsEnabled(services, links), turnstile.SiteKey));
    }

    // Always 202: the answer is the same for an address that applied, one that did not, and one inside its cooldown.
    private static async Task<Results<Accepted, NotFound, ValidationProblem, ProblemHttpResult>> RequestLink(
        string organizationSlug,
        PublicDataRequestLinkRequest request,
        IValidator<PublicDataRequestLinkRequest> validator,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        TenantContext tenant,
        DataRequestLinks links,
        IServiceProvider services,
        IOptions<DataRequestOptions> options,
        TurnstileVerifier turnstile,
        TimeProvider time,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var db = outbox.DbContext;
        if (await PublicOrganizations.FindAsync(organizationSlug, db, tenant, cancellationToken) is not { } organization
            || !IsEnabled(services, links))
        {
            return TypedResults.NotFound();
        }

        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        // Before anything is looked up or sent: this route emails a link to whatever address was typed.
        if (await PublicApplicationDraftEndpoints.BotCheckProblemAsync(
                turnstile,
                request.BotCheckToken,
                http.Connection.RemoteIpAddress?.ToString(),
                cancellationToken,
                "We could not finish the security check, so no link was emailed.") is { } refused)
        {
            return refused;
        }

        var normalized = Candidate.Normalize(request.Email);
        var now = time.GetUtcNow();
        var due = now - options.Value.LinkCooldown;

        // An address that never applied goes through the same steps as one that did, minus the claim and the message.
        // The claim and the message share one transaction, so a link is never claimed without being queued.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var candidateId = await db.Candidates.AsNoTracking()
            .Where(c => c.NormalizedEmail == normalized)
            .Select(c => (Guid?)c.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (candidateId is { } id)
        {
            // Atomic: of simultaneous requests for one address, one row update succeeds and only that one queues the email.
            var claimed = await db.Candidates
                .Where(c => c.Id == id && (c.DataRequestLinkSentAt == null || c.DataRequestLinkSentAt < due))
                .ExecuteUpdateAsync(set => set.SetProperty(c => c.DataRequestLinkSentAt, now), cancellationToken);
            if (claimed == 1)
            {
                await outbox.PublishAsync(new SendDataRequestLink(organization.Id, id, SendDataRequestLink.KeyFor(id, now)));
            }
        }

        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        return TypedResults.Accepted((string?)null);
    }

    private static async Task<Results<Ok<PublicDataRequestLinkStatus>, NotFound>> GetChoices(
        string organizationSlug,
        string token,
        ScreenHireDbContext db,
        TenantContext tenant,
        DataRequestLinks links,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        if (await FindLinkAsync(organizationSlug, token, db, tenant, links, services, cancellationToken) is not { } found)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(new PublicDataRequestLinkStatus(found.Organization.Name));
    }

    // Creates the request, or returns the open one of that kind the candidate already made, so a second click or a second
    // device changes nothing and adds no second entry for the team.
    private static async Task<Results<Ok<PublicDataRequestReceipt>, NotFound, ValidationProblem>> MakeRequest(
        string organizationSlug,
        string token,
        PublicDataRequestChoice choice,
        IValidator<PublicDataRequestChoice> validator,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        TenantContext tenant,
        DataRequestLinks links,
        IServiceProvider services,
        IOptions<DataRequestOptions> options,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var db = outbox.DbContext;
        if (await FindLinkAsync(organizationSlug, token, db, tenant, links, services, cancellationToken) is not { } found)
        {
            return TypedResults.NotFound();
        }

        if (await validator.ProblemsAsync(choice, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        if (await OpenRequestAsync(db, found.CandidateId, choice.Kind, cancellationToken) is { } existing)
        {
            return TypedResults.Ok(ToReceipt(existing));
        }

        var now = time.GetUtcNow();
        var request = DataRequest.Open(found.Organization.Id, found.CandidateId, choice.Kind, now, options.Value.DueIn);
        db.DataRequests.Add(request);
        // The team's log holds the request's id and nothing about the person. No actor: the candidate asked.
        db.MembershipEvents.Add(new MembershipEvent
        {
            Kind = MembershipEventKind.DataRequestReceived,
            OrganizationId = found.Organization.Id,
            ActorUserId = null,
            DataRequestId = request.Id,
            OccurredAt = now,
        });
        await outbox.PublishAsync(new DataRequestReceived(found.Organization.Id, request.Id, request.Kind, now));
        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            // A simultaneous click made the same request first. Nothing of ours was saved, so answer with theirs.
            db.ChangeTracker.Clear();
            return await OpenRequestAsync(db, found.CandidateId, choice.Kind, cancellationToken) is { } winner
                ? TypedResults.Ok(ToReceipt(winner))
                : TypedResults.NotFound();
        }
        catch (DbUpdateException exception) when (exception.IsForeignKeyViolation())
        {
            // The candidate was erased between the check and the save: there is no one left to ask for anything.
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(ToReceipt(request));
    }

    private static async Task<Results<Ok<PublicDataDownload>, NotFound>> GetDownload(
        string organizationSlug,
        string token,
        ScreenHireDbContext db,
        TenantContext tenant,
        DataRequestLinks links,
        IServiceProvider services,
        IDataExportStore exports,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled(services, links)
            || await PublicOrganizations.FindAsync(organizationSlug, db, tenant, cancellationToken) is not { } organization
            || links.ReadDownload(token) is not { } download
            || download.OrganizationId != organization.Id)
        {
            return TypedResults.NotFound();
        }

        var request = await db.DataRequests.AsNoTracking().SingleOrDefaultAsync(r => r.Id == download.RequestId, cancellationToken);
        if (request is not { Status: DataRequestStatus.Completed, ExportBlobName: { } blobName, ExportExpiresAt: { } expiresAt }
            || expiresAt <= time.GetUtcNow()
            || await exports.IssueReadLinkAsync(blobName, DownloadFileName, cancellationToken) is not { } url)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(new PublicDataDownload(url));
    }

    // The flow needs an email to send and a careers address to link to; where either is missing, the careers page says to write to the employer.
    private static bool IsEnabled(IServiceProvider services, DataRequestLinks links) =>
        services.GetService<IEmailSender>() is not null && links.IsConfigured;

    private static Task<DataRequest?> OpenRequestAsync(ScreenHireDbContext db, Guid candidateId, DataRequestKind kind, CancellationToken cancellationToken) =>
        db.DataRequests.AsNoTracking()
            .SingleOrDefaultAsync(r => r.CandidateId == candidateId && r.Kind == kind && r.Status == DataRequestStatus.Open, cancellationToken);

    private static PublicDataRequestReceipt ToReceipt(DataRequest request) => new(request.Id, request.Kind, request.ReceivedAt);

    // The slug's organization as the tenant, the candidate the emailed token names, who must belong to it and still exist.
    // The candidate is read through the tenant-filtered set: the request's foreign key to candidates is a single column,
    // so only a candidate found here is one this organization may attach a request to.
    private static async Task<(Organization Organization, Guid CandidateId)?> FindLinkAsync(
        string organizationSlug, string token, ScreenHireDbContext db, TenantContext tenant, DataRequestLinks links, IServiceProvider services,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled(services, links)
            || await PublicOrganizations.FindAsync(organizationSlug, db, tenant, cancellationToken) is not { } organization
            || links.ReadLink(token) is not { } link
            || link.OrganizationId != organization.Id
            || !await db.Candidates.AsNoTracking().AnyAsync(c => c.Id == link.CandidateId, cancellationToken))
        {
            return null;
        }

        return (organization, link.CandidateId);
    }
}
