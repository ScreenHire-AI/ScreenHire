using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Applications;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Branding;
using ScreenHire.Api.Configuration;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.Applications;
using ScreenHire.Application.TalentPool;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;
using ScreenHire.Domain.TalentPool;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// The talent pool (docs/architecture/talent-pool.md): keeping a candidate in mind, the pool's list, taking someone out,
/// inviting them to apply for another job and adding them to one. People only, like the email routes: no API key, so no
/// integration and no MCP tool acts on it. The tenant and job-scope filters apply as everywhere, so a member on Only jobs
/// they're on sees and acts on the people kept from their own jobs. Nothing here reads verification output (ADR-0004).
/// </summary>
internal static class TalentPoolEndpoints
{
    public static IEndpointRouteBuilder MapTalentPoolEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/talent-pool").WithTags("Talent pool").RequireAuthorization();
        group.MapGet("", ListTalentPool).WithName("ListTalentPool").Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("", KeepInMind).WithName("KeepInMind")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.MoveCandidates)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/{candidateId:guid}/remove", RemoveFromTalentPool).WithName("RemoveFromTalentPool")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.MoveCandidates)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/invitations/preview", PreviewInvitation).WithName("PreviewTalentPoolInvitation")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EmailCandidates)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/invitations", InviteToApply).WithName("InviteToApply")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EmailCandidates)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/applications", AddFromTalentPool).WithName("AddFromTalentPool")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.AddCandidates)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
    }

    // Everyone in the pool whose permission still holds: the daily sweep takes out whoever it ended for, and until it runs
    // they are already off the list. A pool is a few hundred people at most, so it is one read and the rule runs here.
    private static async Task<Ok<TalentPoolList>> ListTalentPool(ScreenHireDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var months = await TalentPoolMembership.MonthsAsync(db, cancellationToken);
        var rows = await db.TalentPoolEntries.AsNoTracking()
            .Join(db.Applications, e => e.ApplicationId, a => a.Id, (e, a) => new { Entry = e, Application = a })
            .Join(db.Jobs, x => x.Application.JobId, j => j.Id, (x, j) => new { x.Entry, x.Application, Job = j })
            .Join(db.Candidates, x => x.Entry.CandidateId, c => c.Id, (x, c) => new { x.Entry, x.Application, x.Job, Candidate = c })
            .ToListAsync(cancellationToken);
        var current = rows.Where(r => r.Application.AllowsFutureRoles(months, now)).ToList();

        var keptEventIds = current.Select(r => r.Entry.KeptEventId).ToList();
        var notes = await db.ApplicationEvents.AsNoTracking()
            .Where(e => keptEventIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.Data.Note, cancellationToken);
        var candidateIds = current.Select(r => r.Entry.CandidateId).ToList();
        var applied = (await db.Applications.AsNoTracking()
                .Where(a => candidateIds.Contains(a.CandidateId))
                .Select(a => new { a.CandidateId, a.JobId })
                .ToListAsync(cancellationToken))
            .ToLookup(a => a.CandidateId, a => a.JobId);
        var invitedJobIds = current.Select(r => r.Entry.LastInvitedJobId).OfType<Guid>().Distinct().ToList();
        var invitedTitles = await db.Jobs.AsNoTracking()
            .Where(j => invitedJobIds.Contains(j.Id))
            .ToDictionaryAsync(j => j.Id, j => j.Title, cancellationToken);

        var people = current
            .OrderBy(r => r.Candidate.FullName, StringComparer.CurrentCultureIgnoreCase)
            .Select(r => new TalentPoolMember(
                r.Candidate.Id, r.Candidate.FullName, r.Candidate.Email, r.Application.Id, r.Job.Id, r.Job.Title,
                notes.GetValueOrDefault(r.Entry.KeptEventId), r.Application.Tags, r.Entry.KeptAt, r.Entry.KeptByUserId,
                r.Application.FutureRolesUntil(months)!.Value, r.Entry.LastInvitedJobId,
                r.Entry.LastInvitedJobId is { } invited ? invitedTitles.GetValueOrDefault(invited) : null, r.Entry.LastInvitedAt,
                [.. applied[r.Candidate.Id].Distinct()]))
            .ToList();
        return TypedResults.Ok(new TalentPoolList(months, people));
    }

    // One transaction for the selection: each kept application, its Kept in mind entry and the pool entry, or nothing. An
    // application that has ended, or whose candidate gave no permission, is counted and left as it is.
    private static async Task<Results<Ok<KeptInMind>, ValidationProblem, ProblemHttpResult>> KeepInMind(
        KeepInMindRequest request,
        IValidator<KeepInMindRequest> validator,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        ITenantContext tenant,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var db = outbox.DbContext;
        var ids = request.ApplicationIds.Distinct().ToList();
        var applications = await db.Applications.Where(a => ids.Contains(a.Id)).ToListAsync(cancellationToken);
        if (applications.Count != ids.Count)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem(
                "applicationIds", "One or more of these applications does not exist, or belongs to another organization."));
        }

        var jobIds = applications.Select(a => a.JobId).Distinct().ToList();
        var jobs = await db.Jobs.AsNoTracking().Where(j => jobIds.Contains(j.Id)).ToDictionaryAsync(j => j.Id, cancellationToken);
        var candidateIds = applications.Select(a => a.CandidateId).Distinct().ToList();
        var entries = await db.TalentPoolEntries.Where(e => candidateIds.Contains(e.CandidateId)).ToDictionaryAsync(e => e.CandidateId, cancellationToken);
        var keptFromIds = entries.Values.Select(e => e.ApplicationId).ToList();
        var keptFrom = await db.Applications.AsNoTracking().Where(a => keptFromIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, cancellationToken);

        var months = await TalentPoolMembership.MonthsAsync(db, cancellationToken);
        var now = time.GetUtcNow();
        var (kept, notAllowed, ended) = (0, 0, 0);
        var joined = new HashSet<Guid>();
        foreach (var application in applications)
        {
            var stage = jobs[application.JobId].Workflow.FindStage(application.CurrentStageId);
            if (application.HasEnded(stage))
            {
                ended++;
                continue;
            }

            if (!application.AllowsFutureRoles(months, now))
            {
                notAllowed++;
                continue;
            }

            var entry = application.KeepInMind(stage, months, now, tenant.UserId, request.Note);
            db.ApplicationEvents.Add(entry);
            kept++;
            if (!joined.Add(application.CandidateId))
            {
                continue; // two of one candidate's applications in one selection: the first stands for them in the pool
            }

            // Kept from another application before: this one stands for them in the pool now.
            if (entries.GetValueOrDefault(application.CandidateId) is { } earlier && keptFrom.GetValueOrDefault(earlier.ApplicationId) is { } earlierApplication)
            {
                await outbox.PublishAsync(TalentPoolMembership.Leave(
                    db, earlier, earlierApplication, TalentPoolExit.KeptFromAnotherJob, now, tenant.UserId, application.JobId, application.Id));
            }

            var member = TalentPoolEntry.For(application, entry);
            db.TalentPoolEntries.Add(member);
            await outbox.PublishAsync(TalentPoolMembership.Changed(member, now));
        }

        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TypedResults.Problem("Someone else changed one of these candidates at the same moment; refresh and try again.", statusCode: StatusCodes.Status409Conflict);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            // The candidate is in the pool from a job this member does not see (ADR-0015), or someone kept them in mind
            // at the same moment.
            return TypedResults.Problem(
                "One of these candidates is already in the talent pool from a job you don't see, or was kept in mind just now.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return TypedResults.Ok(new KeptInMind(kept, notAllowed, ended));
    }

    // With candidateAsked, every permission the candidate gave on an application this member sees is withdrawn too, so
    // nobody keeps them again on it.
    private static async Task<Results<NoContent, NotFound>> RemoveFromTalentPool(
        Guid candidateId,
        RemoveFromTalentPoolRequest request,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        ITenantContext tenant,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var db = outbox.DbContext;
        if (await db.TalentPoolEntries.SingleOrDefaultAsync(e => e.CandidateId == candidateId, cancellationToken) is not { } entry)
        {
            return TypedResults.NotFound();
        }

        var now = time.GetUtcNow();
        var applications = await db.Applications.Where(a => a.CandidateId == candidateId).ToListAsync(cancellationToken);
        if (request.CandidateAsked)
        {
            applications.ForEach(a => a.WithdrawFutureRoles(now));
        }

        var keptFrom = applications.Single(a => a.Id == entry.ApplicationId);
        await outbox.PublishAsync(TalentPoolMembership.Leave(
            db, entry, keptFrom, request.CandidateAsked ? TalentPoolExit.CandidateAsked : TalentPoolExit.Removed, now, tenant.UserId));
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    // The invitation as the candidate would receive it, in the organization's wording now, with the job's real link. The
    // link to stop being kept is made only when the worker sends it, so the preview shows what the team's copy says. Only
    // for someone this member sees in the pool with a permission that holds, as the invitation itself: the entry's job
    // scope keeps the preview from showing the address of a candidate from a job they don't see.
    private static async Task<Results<Ok<EmailPreview>, NotFound>> PreviewInvitation(
        InvitationPreviewRequest request,
        ScreenHireDbContext db,
        EmailTemplates renderer,
        EmailTemplateStore store,
        Application.Branding.EmailHeaders headers,
        IOptions<DomainOptions> domains,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var months = await TalentPoolMembership.MonthsAsync(db, cancellationToken);
        var keptFrom = await db.TalentPoolEntries.AsNoTracking()
            .Where(e => e.CandidateId == request.CandidateId)
            .Join(db.Applications, e => e.ApplicationId, a => a.Id, (e, a) => a)
            .SingleOrDefaultAsync(cancellationToken);
        if (keptFrom is null || !keptFrom.AllowsFutureRoles(months, time.GetUtcNow()))
        {
            return TypedResults.NotFound();
        }

        var candidate = await db.Candidates.AsNoTracking().SingleAsync(c => c.Id == request.CandidateId, cancellationToken);
        var job = await db.Jobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == request.JobId, cancellationToken);
        if (job is null)
        {
            return TypedResults.NotFound();
        }

        var organization = await db.Organizations.AsNoTracking().SingleAsync(o => o.Id == job.OrganizationId, cancellationToken);
        var template = await store.FindAsync(db, EmailTemplateIds.TalentPoolInvitation, cancellationToken);
        var model = ApplicationEmails.ModelFor(candidate, job.Title, organization.Name, stageName: null) with
        {
            JobLink = domains.Value.CareersJobUrl(organization.Slug, job.Slug),
            RemoveLink = ApplicationEmails.QuestionnaireLinkNotKept,
        };
        var header = await headers.ForAsync(db, organization, cancellationToken);
        return TypedResults.Ok(renderer.Render(template!.Content, candidate.Email, model, EmailTemplateIds.TalentPoolInvitation, header).ToPreview());
    }

    // One message per candidate, through the outbox. The worker renders and sends each, and checks again that the candidate
    // is still in the pool and the job still open. Candidates no longer in the pool, or who already applied, are skipped.
    private static async Task<Results<Accepted<InvitationsQueued>, NotFound, ValidationProblem, ProblemHttpResult>> InviteToApply(
        InviteToApplyRequest request,
        IValidator<InviteToApplyRequest> validator,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        ITenantContext tenant,
        TimeProvider time,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        if (services.GetService<IEmailSender>() is null)
        {
            return TypedResults.NotFound(); // no email sender is configured, so inviting is hidden
        }

        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var db = outbox.DbContext;
        var job = await db.Jobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == request.JobId, cancellationToken);
        if (job is null)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("jobId", "There is no job with this id."));
        }

        if (job.Status != JobStatus.Open)
        {
            return TypedResults.Problem("Only an open job takes applications, so invite people to one that is posted.", statusCode: StatusCodes.Status409Conflict);
        }

        var (members, skipped) = await MembersAsync(db, request.CandidateIds, job.Id, time.GetUtcNow(), cancellationToken);
        foreach (var member in members)
        {
            await outbox.PublishAsync(new SendTalentPoolInvitation(
                member.OrganizationId, member.ApplicationId, job.Id, Guid.CreateVersion7(), tenant.UserId!.Value));
        }

        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        return TypedResults.Accepted((string?)null, new InvitationsQueued(members.Count, skipped));
    }

    // Each candidate gets an application on the job, as a recruiter adding them by hand does, carrying the permission they
    // gave on the application they were kept from, so its clock does not restart. Adding them takes them out of the pool
    // (ApplicationSubmission), and the kept application's Activity names the new one. One commit for the selection: a
    // candidate who applied to the job at the same moment fails the whole add with a 409, and nothing is half done.
    private static async Task<Results<Ok<AddedFromTalentPool>, ValidationProblem, ProblemHttpResult>> AddFromTalentPool(
        AddFromTalentPoolRequest request,
        IValidator<AddFromTalentPoolRequest> validator,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        ITenantContext tenant,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var db = outbox.DbContext;
        var job = await db.Jobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == request.JobId, cancellationToken);
        if (job is null)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("jobId", "There is no job with this id."));
        }

        if (job.Status == JobStatus.Closed)
        {
            return TypedResults.Problem("A closed job takes no applications.", statusCode: StatusCodes.Status409Conflict);
        }

        var now = time.GetUtcNow();
        var (members, skipped) = await MembersAsync(db, request.CandidateIds, job.Id, now, cancellationToken);
        var keptFromIds = members.Select(m => m.ApplicationId).ToList();
        var keptFromById = await db.Applications.AsNoTracking().Where(a => keptFromIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, cancellationToken);
        var memberCandidateIds = members.Select(m => m.CandidateId).ToList();
        var candidates = await db.Candidates.AsNoTracking().Where(c => memberCandidateIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken);
        var added = new List<Guid>();
        foreach (var member in members)
        {
            var keptFrom = keptFromById[member.ApplicationId];
            var candidate = candidates[member.CandidateId];
            var submitted = await ApplicationSubmission.SubmitAsync(
                outbox, job, new ApplicationSubmission.CandidateDetails(candidate.FirstName, candidate.LastName, candidate.Email, candidate.Phone),
                new ApplicationDeclarations(false, AiUseDeclaration.NotProvided, null),
                ApplicationIntake.Recruiter with { FutureRoles = keptFrom.CarriedFutureRoles() }, [], now, tenant.UserId, cancellationToken,
                commit: false);
            if (submitted is { } created)
            {
                added.Add(created.Application.Id);
            }
            else
            {
                skipped++;
            }
        }

        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return TypedResults.Problem("One of them applied to this job just now; refresh and try again.", statusCode: StatusCodes.Status409Conflict);
        }

        return TypedResults.Ok(new AddedFromTalentPool(added, skipped));
    }

    // The pool entries of these candidates whose permission still holds and who have no application for the job yet, and
    // how many of the candidates that leaves out.
    private static async Task<(List<TalentPoolEntry> Members, int Skipped)> MembersAsync(
        ScreenHireDbContext db, IReadOnlyList<Guid> candidateIds, Guid jobId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var ids = candidateIds.Distinct().ToList();
        var months = await TalentPoolMembership.MonthsAsync(db, cancellationToken);
        var rows = await db.TalentPoolEntries.AsNoTracking()
            .Where(e => ids.Contains(e.CandidateId))
            .Join(db.Applications, e => e.ApplicationId, a => a.Id, (e, a) => new { Entry = e, Application = a })
            .ToListAsync(cancellationToken);
        var applied = await db.Applications.AsNoTracking()
            .Where(a => a.JobId == jobId && ids.Contains(a.CandidateId))
            .Select(a => a.CandidateId)
            .ToListAsync(cancellationToken);
        var members = rows
            .Where(r => r.Application.AllowsFutureRoles(months, now) && !applied.Contains(r.Entry.CandidateId))
            .Select(r => r.Entry)
            .ToList();
        return (members, ids.Count - members.Count);
    }
}

/// <summary>
/// The careers page a candidate opens from the link in a talent pool invitation, to stop the organization keeping their
/// details for future roles (docs/architecture/talent-pool.md). The token in the path is the signed link the worker put in
/// the invitation (<see cref="TalentPoolLinks"/>). Anonymous, rate limited per address like the apply flow, and called by
/// the careers server. A token for another organization, or one that is forged or damaged, is a 404.
/// </summary>
internal static class PublicTalentPoolEndpoints
{
    public static IEndpointRouteBuilder MapPublicTalentPoolEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/public/organizations/{organizationSlug}/talent-pool/{token}")
            .WithTags("Public careers")
            .AllowAnonymous()
            .RequireRateLimiting(ApplyOptions.RateLimitPolicy);
        group.MapGet("", GetPublicTalentPoolStatus).WithName("GetPublicTalentPoolStatus")
            .Produces(StatusCodes.Status429TooManyRequests);
        group.MapPost("", StopKeepingDetails).WithName("StopKeepingDetails")
            .Produces(StatusCodes.Status429TooManyRequests);
        return app;
    }

    private static async Task<Results<Ok<PublicTalentPoolStatus>, NotFound>> GetPublicTalentPoolStatus(
        string organizationSlug,
        string token,
        ScreenHireDbContext db,
        TenantContext tenant,
        TalentPoolLinks links,
        IOptions<DomainOptions> domains,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (await FindAsync(organizationSlug, token, db, tenant, links, cancellationToken) is not { } found)
        {
            return TypedResults.NotFound();
        }

        var months = await TalentPoolMembership.MonthsAsync(db, cancellationToken);
        var now = time.GetUtcNow();
        var kept = await db.TalentPoolEntries.AnyAsync(e => e.CandidateId == found.CandidateId, cancellationToken)
            || (await db.Applications.AsNoTracking().Where(a => a.CandidateId == found.CandidateId).ToListAsync(cancellationToken))
                .Any(a => a.AllowsFutureRoles(months, now));
        return TypedResults.Ok(new PublicTalentPoolStatus(await found.Organization.ToPublicAsync(db, domains.Value, cancellationToken), kept));
    }

    // Withdraws every permission for future roles the candidate gave the organization and takes them out of the pool, in
    // one transaction. Stopping twice changes nothing.
    private static async Task<Results<NoContent, NotFound>> StopKeepingDetails(
        string organizationSlug,
        string token,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        TenantContext tenant,
        TalentPoolLinks links,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var db = outbox.DbContext;
        if (await FindAsync(organizationSlug, token, db, tenant, links, cancellationToken) is not { } found)
        {
            return TypedResults.NotFound();
        }

        var now = time.GetUtcNow();
        var applications = await db.Applications.Where(a => a.CandidateId == found.CandidateId).ToListAsync(cancellationToken);
        applications.ForEach(a => a.WithdrawFutureRoles(now));
        if (await db.TalentPoolEntries.SingleOrDefaultAsync(e => e.CandidateId == found.CandidateId, cancellationToken) is { } entry)
        {
            var keptFrom = applications.Single(a => a.Id == entry.ApplicationId);
            await outbox.PublishAsync(TalentPoolMembership.Leave(db, entry, keptFrom, TalentPoolExit.CandidateAsked, now, actorUserId: null));
        }

        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<(Organization Organization, Guid CandidateId)?> FindAsync(
        string organizationSlug, string token, ScreenHireDbContext db, TenantContext tenant, TalentPoolLinks links, CancellationToken cancellationToken)
    {
        if (await PublicOrganizations.FindAsync(organizationSlug, db, tenant, cancellationToken) is not { } organization
            || links.Read(token) is not { } link
            || link.OrganizationId != organization.Id
            || !await db.Candidates.AnyAsync(c => c.Id == link.CandidateId, cancellationToken))
        {
            return null;
        }

        return (organization, link.CandidateId);
    }
}
