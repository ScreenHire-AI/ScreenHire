using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Applications;
using ScreenHire.Api.Contracts;
using ScreenHire.Api.Email;
using ScreenHire.Application.Applications;
using ScreenHire.Application.Portal;
using ScreenHire.Application.Scheduling;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;
using ScreenHire.Domain.Scheduling;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// The candidate's page (docs/architecture/candidate-portal.md): ask for a link by email address, open the page with the
/// emailed link, read the messages about an application and answer them. Anonymous, rate limited per address like the apply
/// flow, and called by the careers server. There is no account and no session: the signed link is the whole of the access, and
/// it names one candidate of one organization.
/// </summary>
/// <remarks>
/// <para>
/// Asking for a link answers 202 with no body whether or not the address has applied, so it tells nobody who has. Every route
/// that takes a token gives a 404 for a token that is forged, damaged, expired, made for another organization or for a
/// candidate who has since been erased, and for an application that is not the token's candidate's, so a page cannot tell them apart.
/// </para>
/// <para>
/// What the page may show is fixed here: the job, when the candidate applied, a funnel of four fixed steps, and what is waiting
/// for them. It never carries the employer's stage names or how many stages there are, a rating, a scorecard, a note, a tag or
/// anything about verification (CLAUDE.md non-negotiables 1 to 4). Messages are the emails sent to the candidate and their replies.
/// </para>
/// </remarks>
internal static class PublicPortalEndpoints
{
    public static IEndpointRouteBuilder MapPublicPortalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/public/organizations/{organizationSlug}/portal")
            .WithTags("Public careers")
            .AllowAnonymous()
            .RequireRateLimiting(PortalOptions.RateLimitPolicy);
        group.MapGet("", GetForm).WithName("GetPublicPortalForm")
            .Produces(StatusCodes.Status429TooManyRequests);
        group.MapPost("links", RequestLink).WithName("RequestPublicPortalLink")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status429TooManyRequests);
        group.MapGet("{token}", GetPortal).WithName("GetPublicPortal")
            .Produces(StatusCodes.Status429TooManyRequests);
        group.MapGet("{token}/applications/{applicationId:guid}/messages", GetMessages).WithName("GetPublicPortalMessages")
            .Produces(StatusCodes.Status429TooManyRequests);
        group.MapPost("{token}/applications/{applicationId:guid}/messages", SendReply).WithName("SendPublicPortalReply")
            .WithMetadata(new RequestSizeLimitAttribute(MaxReplyBytes))
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status429TooManyRequests);
        return app;
    }

    /// <summary>
    /// The most bytes a reply may post. The careers server takes at most 12 MB from a form (<c>actionBodySizeLimit</c>), so a
    /// reply's files total at most 10 MB; as base64 text in JSON that is about 13.4 MB, and this leaves room for the words.
    /// </summary>
    internal const int MaxReplyBytes = 16 * 1024 * 1024;

    private static async Task<Results<Ok<PublicPortalForm>, NotFound>> GetForm(
        string organizationSlug,
        ScreenHireDbContext db,
        TenantContext tenant,
        CandidatePortalLinks links,
        IServiceProvider services,
        TurnstileVerifier turnstile,
        CancellationToken cancellationToken)
    {
        if (await PublicOrganizations.FindAsync(organizationSlug, db, tenant, cancellationToken) is not { } organization)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(new PublicPortalForm(organization.Name, IsEnabled(services, links), turnstile.SiteKey));
    }

    // Always 202: the answer is the same for an address that applied, one that did not, and one inside its cooldown.
    private static async Task<Results<Accepted, NotFound, ValidationProblem, ProblemHttpResult>> RequestLink(
        string organizationSlug,
        PublicPortalLinkRequest request,
        IValidator<PublicPortalLinkRequest> validator,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        TenantContext tenant,
        CandidatePortalLinks links,
        IServiceProvider services,
        IOptions<PortalOptions> options,
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
                .Where(c => c.Id == id && (c.PortalLinkSentAt == null || c.PortalLinkSentAt < due))
                .ExecuteUpdateAsync(set => set.SetProperty(c => c.PortalLinkSentAt, now), cancellationToken);
            if (claimed == 1)
            {
                await outbox.PublishAsync(new SendPortalLink(organization.Id, id, SendPortalLink.KeyFor(id, now)));
            }
        }

        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        return TypedResults.Accepted((string?)null);
    }

    private static async Task<Results<Ok<PublicPortal>, NotFound>> GetPortal(
        string organizationSlug,
        string token,
        ScreenHireDbContext db,
        TenantContext tenant,
        CandidatePortalLinks links,
        QuestionnaireLinks questionnaireLinks,
        InterviewLinks interviewLinks,
        ApplicationDrafts drafts,
        IServiceProvider services,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (await FindLinkAsync(organizationSlug, token, db, tenant, links, services, cancellationToken) is not { } found)
        {
            return TypedResults.NotFound();
        }

        var now = time.GetUtcNow();
        var rows = await db.Applications.AsNoTracking()
            .Where(a => a.CandidateId == found.Candidate.Id)
            .Join(db.Jobs, a => a.JobId, j => j.Id, (a, j) => new { Application = a, Job = j })
            .OrderByDescending(x => x.Application.SubmittedAt)
            .ToListAsync(cancellationToken);
        var ids = rows.Select(x => x.Application.Id).ToList();

        var questionnaires = await db.QuestionnaireRequests.AsNoTracking()
            .Where(r => ids.Contains(r.ApplicationId) && r.CompletedAt == null)
            .ToListAsync(cancellationToken);
        var interviews = await db.InterviewRequests.AsNoTracking()
            .Where(i => ids.Contains(i.ApplicationId) && i.Status == InterviewStatus.Open)
            .ToListAsync(cancellationToken);
        var written = (await db.ApplicationEvents.AsNoTracking()
                .Where(e => ids.Contains(e.ApplicationId) && e.Type == ApplicationEventType.EmailSent)
                .Select(e => e.ApplicationId)
                .Distinct()
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var replied = (await db.InboundEmails.AsNoTracking()
                .Where(e => ids.Contains(e.ApplicationId) && !e.AutoReply)
                .Select(e => e.ApplicationId)
                .Distinct()
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var applications = new List<PublicPortalApplication>(rows.Count);
        foreach (var row in rows)
        {
            var application = row.Application;
            var job = row.Job;
            var stage = job.Workflow.FindStage(application.CurrentStageId);
            var funnel = CandidateFunnel.For(application, stage);
            var waiting = new List<PublicPortalWaiting>();
            if (!application.HasEnded(stage))
            {
                // The same rules as the questionnaire and interview pages: what those pages would open is what is listed.
                waiting.AddRange(questionnaires
                    .Where(r => r.ApplicationId == application.Id && !r.IsExpired(now) && job.Status != JobStatus.Closed
                        && job.Workflow.FindFollowUp(r.QuestionnaireId) is not null)
                    .Select(r => new PublicPortalWaiting(PublicPortalWaitingKind.Questionnaire, questionnaireLinks.Issue(r))));
                waiting.AddRange(interviews
                    .Where(i => i.ApplicationId == application.Id && i.EffectiveStatus(now) == InterviewStatus.Open)
                    .Select(i => new PublicPortalWaiting(PublicPortalWaitingKind.Interview, interviewLinks.Issue(i))));
            }

            applications.Add(new PublicPortalApplication(
                application.Id,
                job.Title,
                application.SubmittedAt,
                new PublicPortalFunnel(CandidateFunnel.Steps, funnel.Current, funnel.Ended),
                waiting,
                written.Contains(application.Id) || replied.Contains(application.Id),
                written.Contains(application.Id)));
        }

        // A saved draft is found by the candidate's address: the link proves they received mail there, as a draft's own link does.
        var saved = await db.ApplicationDrafts.AsNoTracking()
            .Where(d => d.Email == found.Candidate.NormalizedEmail && d.ExpiresAt > now)
            .Join(db.Jobs.Where(j => j.Status == JobStatus.Open), d => d.JobId, j => j.Id, (d, j) => new { Draft = d, Job = j })
            .OrderByDescending(x => x.Draft.UpdatedAt)
            .ToListAsync(cancellationToken);
        var draftRows = saved
            .Select(x => new PublicPortalDraft(x.Job.Title, x.Job.Slug, drafts.Issue(x.Draft), x.Draft.ExpiresAt))
            .ToList();

        return TypedResults.Ok(new PublicPortal(found.Organization.Name, found.Candidate.FirstName, applications, draftRows));
    }

    private static async Task<Results<Ok<PublicPortalMessages>, NotFound>> GetMessages(
        string organizationSlug,
        string token,
        Guid applicationId,
        ScreenHireDbContext db,
        TenantContext tenant,
        CandidatePortalLinks links,
        IServiceProvider services,
        IOptions<PortalOptions> options,
        CancellationToken cancellationToken)
    {
        if (await FindLinkAsync(organizationSlug, token, db, tenant, links, services, cancellationToken) is not { } found
            || await FindApplicationAsync(db, found.Candidate.Id, applicationId, cancellationToken) is not { } application)
        {
            return TypedResults.NotFound();
        }

        // Only emails that went: the copy of one the worker decided not to send is deleted, but the timeline entry is the proof.
        var sent = await db.ApplicationEmails.AsNoTracking()
            .Where(e => e.ApplicationId == applicationId)
            .Join(
                db.ApplicationEvents.Where(e => e.ApplicationId == applicationId && e.Type == ApplicationEventType.EmailSent),
                email => email.Id,
                entry => entry.Id,
                (email, entry) => new { email.Subject, email.TextBody, entry.OccurredAt })
            .ToListAsync(cancellationToken);
        var replies = await db.InboundEmails.AsNoTracking()
            .Where(e => e.ApplicationId == applicationId && !e.AutoReply)
            .Select(e => new { e.Id, e.Subject, e.TextBody, e.ReceivedAt })
            .ToListAsync(cancellationToken);
        var replyIds = replies.Select(r => r.Id).ToList();
        var files = await db.InboundEmailAttachments.AsNoTracking()
            .Where(a => replyIds.Contains(a.InboundEmailId))
            .GroupBy(a => a.InboundEmailId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Id, g => g.Count, cancellationToken);

        var messages = sent
            .Select(m => new PublicPortalMessage(
                PublicPortalMessageDirection.FromTeam, m.Subject, PortalMessageText.ForCandidate(m.TextBody), m.OccurredAt, 0))
            .Concat(replies.Select(r => new PublicPortalMessage(
                PublicPortalMessageDirection.FromYou, r.Subject, r.TextBody, r.ReceivedAt, files.GetValueOrDefault(r.Id))))
            .OrderBy(m => m.At)
            .ToList();
        return TypedResults.Ok(new PublicPortalMessages(
            application.JobTitle, messages, sent.Count > 0, options.Value.ReplyBodyMax, PublicPortalReplyRequestValidator.MaxAttachments));
    }

    // The reply goes through the same ingest as an emailed one: a reply row, its timeline entry, its attachments' scans and the
    // event that tells the person who sent the original. It answers the latest email that went, which is the one the person is
    // told about. It does not clear a flagged address (a page says nothing about the mailbox).
    private static async Task<Results<Accepted, NotFound, ValidationProblem, ProblemHttpResult>> SendReply(
        string organizationSlug,
        string token,
        Guid applicationId,
        PublicPortalReplyRequest request,
        IValidator<PublicPortalReplyRequest> validator,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        TenantContext tenant,
        CandidatePortalLinks links,
        InboundEmailIngest ingest,
        IServiceProvider services,
        IOptions<PortalOptions> options,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var db = outbox.DbContext;
        if (await FindLinkAsync(organizationSlug, token, db, tenant, links, services, cancellationToken) is not { } found
            || await FindApplicationAsync(db, found.Candidate.Id, applicationId, cancellationToken) is not { } application)
        {
            return TypedResults.NotFound();
        }

        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var attachments = new List<InboundAttachment>();
        foreach (var file in request.Attachments ?? [])
        {
            try
            {
                attachments.Add(new InboundAttachment(file.FileName, Convert.FromBase64String(file.ContentBase64)));
            }
            catch (FormatException)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["attachments"] = ["A file could not be read. Attach it again."] });
            }
        }

        var latest = await db.ApplicationEmails.AsNoTracking()
            .Where(e => e.ApplicationId == applicationId)
            .Join(
                db.ApplicationEvents.Where(e => e.ApplicationId == applicationId && e.Type == ApplicationEventType.EmailSent),
                email => email.Id,
                entry => entry.Id,
                (email, entry) => new { email.Id, email.Subject, entry.OccurredAt })
            .OrderByDescending(x => x.OccurredAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (latest is null)
        {
            return TypedResults.Problem(
                "The team has not written to you about this application yet, so there is nothing to reply to.",
                statusCode: StatusCodes.Status409Conflict);
        }

        // A leaked link must not be a way to flood the people who are told about each reply.
        var since = time.GetUtcNow() - TimeSpan.FromDays(1);
        if (await db.InboundEmails.CountAsync(
                e => e.ApplicationId == applicationId && e.MessageId != null && e.MessageId.StartsWith(PortalMessageText.MessageIdPrefix) && e.ReceivedAt > since,
                cancellationToken) >= options.Value.RepliesPerDay)
        {
            return TypedResults.Problem(
                "You have sent several messages today. Wait a while, or reply to the email you were sent.",
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        var mail = new ParsedInboundEmail(
            [],
            found.Candidate.Email,
            found.Candidate.FullName,
            PortalMessageText.ReplySubject(latest.Subject),
            request.Body.Trim(),
            Html: null,
            PortalMessageText.MessageIdFor(request.Key),
            AutoReply: false,
            attachments);
        var outcome = await ingest.IngestForAsync(
            new ReplyTarget(found.Organization.Id, latest.Id), mail, outbox, clearsEmailIssue: false, cancellationToken);
        return outcome == InboundOutcome.Ignored ? TypedResults.NotFound() : TypedResults.Accepted((string?)null);
    }

    // The flow needs an email to send and a careers address to link to; where either is missing, the page says it is not available.
    private static bool IsEnabled(IServiceProvider services, CandidatePortalLinks links) =>
        services.GetService<IEmailSender>() is not null && links.IsConfigured;

    // The application, only if it is this candidate's, and its job's title.
    private static Task<ApplicationSummary?> FindApplicationAsync(ScreenHireDbContext db, Guid candidateId, Guid applicationId, CancellationToken cancellationToken) =>
        db.Applications.AsNoTracking()
            .Where(a => a.Id == applicationId && a.CandidateId == candidateId)
            .Join(db.Jobs, a => a.JobId, j => j.Id, (a, j) => new ApplicationSummary(a.Id, j.Title))
            .SingleOrDefaultAsync(cancellationToken);

    private sealed record ApplicationSummary(Guid Id, string JobTitle);

    // The slug's organization as the tenant, the candidate the emailed token names, who must belong to it and still exist.
    private static async Task<(Organization Organization, Candidate Candidate)?> FindLinkAsync(
        string organizationSlug, string token, ScreenHireDbContext db, TenantContext tenant, CandidatePortalLinks links, IServiceProvider services,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled(services, links)
            || await PublicOrganizations.FindAsync(organizationSlug, db, tenant, cancellationToken) is not { } organization
            || links.Read(token) is not { } link
            || link.OrganizationId != organization.Id
            || await db.Candidates.AsNoTracking().SingleOrDefaultAsync(c => c.Id == link.CandidateId, cancellationToken) is not { } candidate)
        {
            return null;
        }

        return (organization, candidate);
    }
}
