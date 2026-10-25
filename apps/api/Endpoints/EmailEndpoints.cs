using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Api.Hiring;
using ScreenHire.Application.Applications;
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
/// timeline once it is sent. The rejection email is never sent here: only a Reject action sends it
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
        emails.MapGet("/preview", PreviewApplicationEmail).WithName("PreviewApplicationEmail").Produces(StatusCodes.Status401Unauthorized);
        emails.MapPost("", SendEmails).WithName("SendEmails")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EmailCandidates)
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

    // A template as one application's candidate would receive it, before a person sends it.
    private static async Task<Results<Ok<EmailPreview>, NotFound, ValidationProblem>> PreviewApplicationEmail(
        Guid applicationId, string templateId, ScreenHireDbContext db, EmailTemplateStore store, EmailTemplates renderer, CancellationToken cancellationToken)
    {
        if (templateId == EmailTemplateIds.ApplicationRejected)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("templateId", RejectionOnly));
        }

        if (templateId == EmailTemplateIds.QuestionnaireRequest)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("templateId", QuestionnaireOnly));
        }

        var row = await db.Applications.AsNoTracking()
            .Where(a => a.Id == applicationId)
            .Join(db.Jobs, a => a.JobId, j => j.Id, (a, j) => new { Application = a, Job = j })
            .Join(db.Candidates, x => x.Application.CandidateId, c => c.Id, (x, c) => new { x.Application, x.Job, Candidate = c })
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null || await store.FindAsync(db, templateId, cancellationToken) is not { } template)
        {
            return TypedResults.NotFound();
        }

        var organization = await db.Organizations.AsNoTracking().SingleAsync(o => o.Id == row.Application.OrganizationId, cancellationToken);
        var model = new EmailTemplateModel(
            row.Candidate.FullName, row.Job.Title, organization.Name, row.Job.Workflow.FindStage(row.Application.CurrentStageId)?.Name);
        return TypedResults.Ok(renderer.Render(template.Content, row.Candidate.Email, model).ToPreview());
    }

    // One message per application, in one transaction with nothing else: the worker sends each and records it on the
    // timeline. The stage-change email skips applications that have ended, as the worker would.
    private static async Task<Results<Accepted<EmailsQueued>, ValidationProblem, NotFound>> SendEmails(
        SendEmailsRequest request,
        IValidator<SendEmailsRequest> validator,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        EmailTemplateStore store,
        ITenantContext tenant,
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
        if (await store.FindAsync(db, request.TemplateId, cancellationToken) is null)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("templateId", "There is no email template with this id."));
        }

        var ids = request.ApplicationIds.Distinct().ToList();
        var applications = await db.Applications.AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .Join(db.Jobs, a => a.JobId, j => j.Id, (a, j) => new { Application = a, j.Workflow })
            .ToListAsync(cancellationToken);
        if (applications.Count != ids.Count)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem(
                "applicationIds", "One or more of these applications does not exist, or belongs to another organization."));
        }

        var skipped = 0;
        foreach (var row in applications)
        {
            if (ApplicationEmails.Skips(request.TemplateId, row.Workflow.FindStage(row.Application.CurrentStageId)))
            {
                skipped++;
                continue;
            }

            await outbox.PublishAsync(new SendApplicationEmail(
                row.Application.OrganizationId, row.Application.Id, request.TemplateId, Guid.CreateVersion7(), tenant.UserId!.Value));
        }

        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        return TypedResults.Accepted("/v1/emails", new EmailsQueued(applications.Count - skipped, skipped));
    }

    private const string RejectionOnly = "The rejection email goes only with a Reject action, which tells the candidate why.";

    private const string QuestionnaireOnly = "The follow-up questions email goes only with a SendQuestionnaire action, which makes its link.";

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
