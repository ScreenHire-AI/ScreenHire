using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Api.Hiring;
using ScreenHire.Application.Applications;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.History;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Persistence.History;
using ScreenHire.Infrastructure.Tenancy;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// The organization's email templates (Settings) and email to candidates, by hand or to a selection. Every member reads
/// the templates, because recruiters pick them in automations and when sending; owners and admins change them. Sends
/// go through the outbox to the worker, one message per candidate, and each email lands on its application's
/// timeline once it is sent. Each email is rendered for its candidate in the request and kept as it goes
/// (<c>application_emails</c>), so Messages shows it as it went. The rejection email is never sent here: only a Reject action sends it
/// (workflow-engine.md §2). These routes take no API key: emailing candidates is a person's action.
/// </summary>
internal static class EmailEndpoints
{
    /// <summary>Templates of an organization's own, beside the built-in ones.</summary>
    public const int MaxTemplates = 100;

    public static IEndpointRouteBuilder MapEmailEndpoints(this IEndpointRouteBuilder app)
    {
        var templates = app.MapGroup("/v1/email-templates").WithTags("Email").RequireAuthorization();
        templates.MapGet("", ListEmailTemplates).WithName("ListEmailTemplates").Produces(StatusCodes.Status401Unauthorized);
        templates.MapPost("", CreateEmailTemplate).WithName("CreateEmailTemplate")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EditEmailTemplates)
            .Produces(StatusCodes.Status401Unauthorized);
        templates.MapGet("/{templateId}", GetEmailTemplate).WithName("GetEmailTemplate").Produces(StatusCodes.Status401Unauthorized);
        templates.MapGet("/{templateId}/history", GetEmailTemplateHistory).WithName("GetEmailTemplateHistory").Produces(StatusCodes.Status401Unauthorized);
        templates.MapPut("/{templateId}", UpdateEmailTemplate).WithName("UpdateEmailTemplate")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EditEmailTemplates)
            .Produces(StatusCodes.Status401Unauthorized);
        templates.MapDelete("/{templateId}", DeleteEmailTemplate).WithName("DeleteEmailTemplate")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EditEmailTemplates)
            .Produces(StatusCodes.Status401Unauthorized);
        templates.MapPost("/preview", PreviewEmailTemplate).WithName("PreviewEmailTemplate")
            .RequireAntiforgeryToken()
            .Produces(StatusCodes.Status401Unauthorized);
        templates.MapPost("/test", SendTestEmail).WithName("SendTestEmail")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EditEmailTemplates)
            .Produces(StatusCodes.Status401Unauthorized);

        var emails = app.MapGroup("/v1/emails").WithTags("Email").RequireAuthorization();
        emails.MapPost("/preview", PreviewApplicationEmail).WithName("PreviewApplicationEmail")
            .RequireAntiforgeryToken()
            .Produces(StatusCodes.Status401Unauthorized);
        emails.MapPost("", SendEmails).WithName("SendEmails")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EmailCandidates)
            .Produces(StatusCodes.Status401Unauthorized);
        emails.MapGet("/{emailId:guid}", GetSentEmail).WithName("GetSentEmail").Produces(StatusCodes.Status401Unauthorized);

        // Under the candidate's path, for the dashboard's Messages tab, but in this people-only group: unlike the
        // candidates routes, it takes no API key.
        var candidates = app.MapGroup("/v1/candidates").WithTags("Email").RequireAuthorization();
        candidates.MapGet("/{candidateId:guid}/emails", ListCandidateEmails).WithName("ListCandidateEmails")
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
    }

    private static async Task<Ok<EmailTemplateList>> ListEmailTemplates(
        ScreenHireDbContext db, EmailTemplateStore store, IServiceProvider services, CancellationToken cancellationToken)
    {
        var list = await store.ListAsync(db, cancellationToken);
        var changes = await ChangeHistoryQueries.LastAsync(db, ChangeSubjectKind.EmailTemplate, [.. list.Select(t => t.Id)], cancellationToken);
        return TypedResults.Ok(new EmailTemplateList(
            SendingEnabled(services),
            [.. list.Select(t => t.ToEntry(changes.GetValueOrDefault(t.Id)))],
            [
                .. EmailTemplates.Variables.Select(v => new EmailTemplateVariableEntry(v.Name, v.Label, false)),
                new(EmailTemplates.Reason.Name, EmailTemplates.Reason.Label, true),
                new(EmailTemplates.QuestionnaireLink.Name, EmailTemplates.QuestionnaireLink.Label, false, QuestionnaireOnly: true),
            ]));
    }

    private static async Task<Results<Ok<EmailTemplateEntry>, NotFound>> GetEmailTemplate(
        string templateId, ScreenHireDbContext db, EmailTemplateStore store, CancellationToken cancellationToken) =>
        await store.FindAsync(db, templateId, cancellationToken) is { } template
            ? TypedResults.Ok(await EntryAsync(db, template, cancellationToken))
            : TypedResults.NotFound();

    // Every change to the template, newest first: a built-in's history outlives its row, which restoring deletes.
    private static async Task<Results<Ok<HistoryPage>, NotFound, ValidationProblem>> GetEmailTemplateHistory(
        string templateId, Guid? before, int? limit, ScreenHireDbContext db, EmailTemplateStore store, CancellationToken cancellationToken)
    {
        if (ChangeHistoryRoutes.LimitProblem(limit) is { } problem)
        {
            return problem;
        }

        if (await store.FindAsync(db, templateId, cancellationToken) is null)
        {
            return TypedResults.NotFound();
        }

        return await ChangeHistoryRoutes.PageAsync(db, ChangeSubjectKind.EmailTemplate, templateId, before, limit, cancellationToken) is { } page
            ? TypedResults.Ok(page)
            : ChangeHistoryRoutes.CursorProblem();
    }

    private static async Task<EmailTemplateEntry> EntryAsync(ScreenHireDbContext db, OrganizationEmailTemplate template, CancellationToken cancellationToken) =>
        template.ToEntry(await ChangeHistoryQueries.LastAsync(db, ChangeSubjectKind.EmailTemplate, template.Id, cancellationToken));

    private static async Task<Results<Created<EmailTemplateEntry>, ValidationProblem, ProblemHttpResult>> CreateEmailTemplate(
        CreateEmailTemplateRequest request,
        IValidator<CreateEmailTemplateRequest> validator,
        ScreenHireDbContext db,
        EmailTemplates renderer,
        EmailTemplateStore store,
        ITenantContext tenant,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        if (renderer.Validate(null, new(request.Subject, request.Body)).ToProblems() is { } wording)
        {
            return TypedResults.ValidationProblem(wording);
        }

        if (await db.EmailTemplates.CountAsync(t => t.BuiltInId == null, cancellationToken) >= MaxTemplates)
        {
            return TypedResults.Problem(
                title: "Too many templates", detail: $"An organization has at most {MaxTemplates} templates of its own. Delete one first.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var template = EmailTemplate.CreateCustom(tenant.OrganizationId!.Value, request.Name.Trim(), request.Subject, request.Body, time.GetUtcNow());
        db.EmailTemplates.Add(template);
        await db.SaveChangesAsync(cancellationToken);
        var created = await store.FindAsync(db, template.Key, cancellationToken);
        return TypedResults.Created($"/v1/email-templates/{template.Key}", await EntryAsync(db, created!, cancellationToken));
    }

    // A built-in's first edit adds the organization's row; later edits change it.
    private static async Task<Results<Ok<EmailTemplateEntry>, NotFound, ValidationProblem>> UpdateEmailTemplate(
        string templateId,
        UpdateEmailTemplateRequest request,
        IValidator<UpdateEmailTemplateRequest> validator,
        ScreenHireDbContext db,
        EmailTemplates renderer,
        EmailTemplateStore store,
        ITenantContext tenant,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var builtIn = EmailTemplateIds.All.Contains(templateId);
        var row = builtIn
            ? await db.EmailTemplates.SingleOrDefaultAsync(t => t.BuiltInId == templateId, cancellationToken)
            : Guid.TryParseExact(templateId, "D", out var rowId)
                ? await db.EmailTemplates.SingleOrDefaultAsync(t => t.Id == rowId && t.BuiltInId == null, cancellationToken)
                : null;
        if (row is null && !builtIn)
        {
            return TypedResults.NotFound();
        }

        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        if (builtIn && request.Name is not null)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("name", "A built-in template keeps its name; leave it out."));
        }

        if (renderer.Validate(templateId, new(request.Subject, request.Body)).ToProblems() is { } wording)
        {
            return TypedResults.ValidationProblem(wording);
        }

        var now = time.GetUtcNow();
        if (row is null)
        {
            db.EmailTemplates.Add(EmailTemplate.Reword(tenant.OrganizationId!.Value, templateId, request.Subject, request.Body, now));
        }
        else
        {
            row.Name = builtIn ? null : request.Name?.Trim() ?? row.Name;
            row.Subject = request.Subject;
            row.Body = request.Body;
            row.UpdatedAt = now;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        // Another person's first edit of this built-in added its row between the read above and this insert. This edit
        // goes on top of theirs, as any later edit would.
        catch (DbUpdateException exception) when (row is null && exception.IsUniqueViolation())
        {
            db.ChangeTracker.Clear();
            var added = await db.EmailTemplates.SingleAsync(t => t.BuiltInId == templateId, cancellationToken);
            added.Subject = request.Subject;
            added.Body = request.Body;
            added.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.Ok(await EntryAsync(db, (await store.FindAsync(db, templateId, cancellationToken))!, cancellationToken));
    }

    // An organization template goes, unless an automation names it. A built-in goes back to ScreenHire's wording. Both
    // delete through the change tracker, not ExecuteDelete, so the change history records them.
    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteEmailTemplate(
        string templateId, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        if (EmailTemplateIds.All.Contains(templateId))
        {
            if (await db.EmailTemplates.SingleOrDefaultAsync(t => t.BuiltInId == templateId, cancellationToken) is { } reworded)
            {
                db.EmailTemplates.Remove(reworded);
                await SaveDeletionAsync(db, cancellationToken);
            }

            return TypedResults.NoContent();
        }

        var row = Guid.TryParseExact(templateId, "D", out var rowId)
            ? await db.EmailTemplates.SingleOrDefaultAsync(t => t.Id == rowId && t.BuiltInId == null, cancellationToken)
            : null;
        if (row is null)
        {
            return TypedResults.NotFound();
        }

        if (await EmailTemplateReferences.UsesAsync(db, templateId, cancellationToken) is { Count: > 0 } uses)
        {
            return TypedResults.Problem(
                title: "Template in use",
                detail: $"An automation in {string.Join(", ", uses.Distinct().Order(StringComparer.CurrentCulture))} sends this template. Change the automation first.",
                statusCode: StatusCodes.Status409Conflict);
        }

        db.EmailTemplates.Remove(row);
        await SaveDeletionAsync(db, cancellationToken);
        return TypedResults.NoContent();
    }

    // Someone else deleted the row at the same moment: the template is gone either way, and their deletion is recorded.
    private static async Task SaveDeletionAsync(ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
        }
    }

    // Unsaved wording, with the sample candidate and the organization's own name.
    private static async Task<Results<Ok<EmailPreview>, ValidationProblem>> PreviewEmailTemplate(
        EmailTemplateDraft request,
        IValidator<EmailTemplateDraft> validator,
        ScreenHireDbContext db,
        EmailTemplates renderer,
        ITenantContext tenant,
        CancellationToken cancellationToken)
    {
        if (await RenderDraftAsync(request, validator, db, renderer, tenant, "preview@example.com", cancellationToken) is not { } message)
        {
            return TypedResults.ValidationProblem(await DraftProblemsAsync(request, validator, renderer, cancellationToken));
        }

        return TypedResults.Ok(message.ToPreview());
    }

    // Unsaved wording, with the sample candidate, to the signed-in person's own address and nobody else's. Sent from the
    // API, as the sign-in link is: the person waits for it and sees a failure at once.
    private static async Task<Results<Ok<TestEmailSent>, ValidationProblem, NotFound>> SendTestEmail(
        EmailTemplateDraft request,
        IValidator<EmailTemplateDraft> validator,
        ScreenHireDbContext db,
        EmailTemplates renderer,
        ITenantContext tenant,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        if (services.GetService<IEmailSender>() is not { } sender)
        {
            return TypedResults.NotFound(); // no email sender is configured, so a test send is hidden
        }

        var to = await db.Users.AsNoTracking().Where(u => u.Id == tenant.UserId).Select(u => u.Email).SingleAsync(cancellationToken);
        if (to is null || await RenderDraftAsync(request, validator, db, renderer, tenant, to, cancellationToken) is not { } message)
        {
            return TypedResults.ValidationProblem(await DraftProblemsAsync(request, validator, renderer, cancellationToken));
        }

        await sender.SendAsync(message with { Subject = $"[Test] {message.Subject}" }, cancellationToken);
        return TypedResults.Ok(new TestEmailSent(to));
    }

    // Wording as one application's candidate would receive it, before a person sends it: the composer's preview. The
    // wording may use the general variables only, as a send by hand does.
    private static async Task<Results<Ok<EmailPreview>, NotFound, ValidationProblem>> PreviewApplicationEmail(
        ApplicationEmailDraft request,
        IValidator<ApplicationEmailDraft> validator,
        ScreenHireDbContext db,
        EmailTemplates renderer,
        CancellationToken cancellationToken)
    {
        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var content = new EmailTemplateContent(request.Subject, request.Body);
        if (renderer.Validate(null, content).ToProblems() is { } wording)
        {
            return TypedResults.ValidationProblem(wording);
        }

        var row = await db.Applications.AsNoTracking()
            .Where(a => a.Id == request.ApplicationId)
            .Join(db.Jobs, a => a.JobId, j => j.Id, (a, j) => new { Application = a, Job = j })
            .Join(db.Candidates, x => x.Application.CandidateId, c => c.Id, (x, c) => new { x.Application, x.Job, Candidate = c })
            .Join(db.Organizations, x => x.Application.OrganizationId, o => o.Id, (x, o) => new { x.Application, x.Job, x.Candidate, Organization = o.Name })
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return TypedResults.NotFound();
        }

        var model = ApplicationEmails.ModelFor(
            row.Candidate, row.Job.Title, row.Organization, row.Job.Workflow.FindStage(row.Application.CurrentStageId)?.Name);
        return TypedResults.Ok(renderer.Render(content, row.Candidate.Email, model).ToPreview());
    }

    // One copy and one message per application, in one transaction with nothing else. Each copy is the email rendered
    // for its candidate now, as the preview showed it; the message carries ids only, because wording a person typed can
    // hold personal data (messaging.md). The worker sends each copy and records it on the timeline. The stage-change
    // email skips applications that have ended, as the worker would.
    private static async Task<Results<Accepted<EmailsQueued>, ValidationProblem, NotFound>> SendEmails(
        SendEmailsRequest request,
        IValidator<SendEmailsRequest> validator,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        EmailTemplateStore store,
        EmailTemplates renderer,
        ITenantContext tenant,
        TimeProvider time,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        if (!SendingEnabled(services))
        {
            return TypedResults.NotFound(); // no email sender is configured, so sending is hidden
        }

        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var db = outbox.DbContext;
        var template = request.TemplateId is { } templateId ? await store.FindAsync(db, templateId, cancellationToken) : null;
        if (request.TemplateId is not null && template is null)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("templateId", "There is no email template with this id."));
        }

        // Wording sent with the request is this send's own (a template changed for one send, or a blank email); without
        // it, the template's wording as it is. The validator requires one or the other.
        var content = request.Subject is { } subject && request.Body is { } body ? new EmailTemplateContent(subject, body) : template!.Content;
        if (request.Subject is not null && renderer.Validate(null, content).ToProblems() is { } wording)
        {
            return TypedResults.ValidationProblem(wording);
        }

        var ids = request.ApplicationIds.Distinct().ToList();
        var rows = await db.Applications.AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .Join(db.Jobs, a => a.JobId, j => j.Id, (a, j) => new { Application = a, Job = j })
            .Join(db.Candidates, x => x.Application.CandidateId, c => c.Id, (x, c) => new { x.Application, x.Job, Candidate = c })
            .ToListAsync(cancellationToken);
        if (rows.Count != ids.Count)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem(
                "applicationIds", "One or more of these applications does not exist, or belongs to another organization."));
        }

        var organization = await db.Organizations.AsNoTracking().Where(o => o.Id == tenant.OrganizationId).Select(o => o.Name).SingleAsync(cancellationToken);
        var now = time.GetUtcNow();
        var skipped = 0;
        foreach (var row in rows)
        {
            var stage = row.Job.Workflow.FindStage(row.Application.CurrentStageId);
            if (ApplicationEmails.Skips(request.TemplateId, stage))
            {
                skipped++;
                continue;
            }

            var copy = ApplicationEmails.Prepare(
                renderer, row.Application, row.Candidate, row.Job.Title, organization, stage?.Name, request.TemplateId, content,
                Guid.CreateVersion7(), now);
            db.ApplicationEmails.Add(copy);
            await outbox.PublishAsync(new SendApplicationEmail(row.Application.OrganizationId, row.Application.Id, copy.Id, tenant.UserId!.Value));
        }

        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        return TypedResults.Accepted("/v1/emails", new EmailsQueued(rows.Count - skipped, skipped));
    }

    // An email exactly as it went. Only once it went: a copy still waiting for the worker, or one the worker decided not to
    // send, is not an email the candidate got.
    private static async Task<Results<Ok<SentEmail>, NotFound>> GetSentEmail(Guid emailId, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var sent = await db.ApplicationEmails.AsNoTracking()
            .Where(e => e.Id == emailId)
            .Join(db.ApplicationEvents.Where(x => x.Type == ApplicationEventType.EmailSent), e => e.Id, x => x.Id, (e, x) => new { Email = e, x.OccurredAt })
            .SingleOrDefaultAsync(cancellationToken);
        return sent is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new SentEmail(
                sent.Email.Id, sent.Email.ApplicationId, sent.Email.TemplateId, sent.Email.To, sent.Email.Subject, sent.Email.HtmlBody,
                sent.Email.TextBody, sent.OccurredAt));
    }

    // Every email that went to the candidate, about any of their applications, newest first: the Messages tab. Each is
    // its timeline entry, with whether its copy was kept (emails sent before copies were kept have only a subject).
    private static async Task<Results<Ok<List<CandidateEmailEntry>>, NotFound>> ListCandidateEmails(
        Guid candidateId, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.Candidates.AnyAsync(c => c.Id == candidateId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var entries = await (
            from entry in db.ApplicationEvents.AsNoTracking()
            join application in db.Applications on entry.ApplicationId equals application.Id
            join job in db.Jobs on application.JobId equals job.Id
            where application.CandidateId == candidateId && entry.Type == ApplicationEventType.EmailSent
            orderby entry.OccurredAt descending, entry.Id descending
            select new
            {
                entry.Id,
                entry.ApplicationId,
                JobTitle = job.Title,
                entry.Data,
                entry.ActorUserId,
                entry.OccurredAt,
                Kept = db.ApplicationEmails.Any(e => e.Id == entry.Id),
            }).ToListAsync(cancellationToken);
        return TypedResults.Ok(entries
            .Select(e => new CandidateEmailEntry(
                e.Id, e.ApplicationId, e.JobTitle, e.Data.EmailTemplateId, e.Data.EmailSubject ?? "", e.ActorUserId, e.Data.AutomationRunId,
                e.OccurredAt, e.Kept))
            .ToList());
    }

    private static bool SendingEnabled(IServiceProvider services) => services.GetService<IEmailSender>() is not null;

    private static async Task<EmailMessage?> RenderDraftAsync(
        EmailTemplateDraft request,
        IValidator<EmailTemplateDraft> validator,
        ScreenHireDbContext db,
        EmailTemplates renderer,
        ITenantContext tenant,
        string to,
        CancellationToken cancellationToken)
    {
        if (!(await validator.ValidateAsync(request, cancellationToken)).IsValid
            || renderer.Validate(request.TemplateId, new(request.Subject, request.Body)).Count > 0)
        {
            return null;
        }

        var organization = await db.Organizations.AsNoTracking().Where(o => o.Id == tenant.OrganizationId).Select(o => o.Name).SingleAsync(cancellationToken);
        return renderer.Render(new EmailTemplateContent(request.Subject, request.Body), to, EmailTemplateModel.Sample(organization), request.TemplateId);
    }

    private static async Task<IDictionary<string, string[]>> DraftProblemsAsync(
        EmailTemplateDraft request, IValidator<EmailTemplateDraft> validator, EmailTemplates renderer, CancellationToken cancellationToken) =>
        await validator.ProblemsAsync(request, cancellationToken)
            ?? renderer.Validate(request.TemplateId, new(request.Subject, request.Body)).ToProblems()
            ?? ValidationExtensions.Problem("", "Your account has no email address.");
}
