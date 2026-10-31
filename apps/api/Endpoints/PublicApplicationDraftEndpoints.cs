using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Applications;
using ScreenHire.Api.Configuration;
using ScreenHire.Api.Contracts;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Jobs;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// Saving the careers apply form to finish later (apply-flow.md §7): a new draft with its link emailed to the candidate,
/// saving it again by its link, and reading it to fill the form. Anonymous, rate limited per candidate address like the
/// apply flow, and called by the careers server.
/// </summary>
/// <remarks>
/// <para>
/// The slug's organization becomes the tenant, as on every public route, and a token must name that organization: one
/// for another organization, or one that is forged, damaged or long past its expiry, is a 404. An expired draft, or one
/// whose job has closed, is a 410, so the page can say so plainly.
/// </para>
/// <para>
/// A draft is never an application. Saving one raises no event and runs no automation or integrity check, so a knockout
/// answer on an early page never ends anything before the candidate sends it (CLAUDE.md non-negotiable 2). Saving a new
/// draft answers the same whether or not the address applied or saved before, so it tells nobody who applied.
/// </para>
/// </remarks>
internal static class PublicApplicationDraftEndpoints
{
    public static IEndpointRouteBuilder MapPublicApplicationDraftEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/public/organizations/{organizationSlug}/jobs/{jobId:guid}/application-drafts", CreateDraft)
            .WithTags("Public careers")
            .AllowAnonymous()
            .RequireRateLimiting(ApplyOptions.RateLimitPolicy)
            .WithName("CreatePublicApplicationDraft")
            .ProducesProblem(StatusCodes.Status410Gone)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        var group = app.MapGroup("/v1/public/organizations/{organizationSlug}/application-drafts/{token}")
            .WithTags("Public careers")
            .AllowAnonymous()
            .RequireRateLimiting(ApplyOptions.RateLimitPolicy);
        group.MapGet("", GetDraft).WithName("GetPublicApplicationDraft")
            .ProducesProblem(StatusCodes.Status410Gone)
            .Produces(StatusCodes.Status429TooManyRequests);
        group.MapPut("", SaveDraft).WithName("SavePublicApplicationDraft")
            .ProducesProblem(StatusCodes.Status410Gone)
            .Produces(StatusCodes.Status429TooManyRequests);
        return app;
    }

    private static async Task<Results<Created<PublicApplicationDraftReceipt>, NotFound, ValidationProblem, ProblemHttpResult>> CreateDraft(
        string organizationSlug,
        Guid jobId,
        PublicApplicationDraftRequest request,
        IValidator<PublicApplicationDraftRequest> validator,
        ScreenHireDbContext db,
        TenantContext tenant,
        ApplicationDrafts drafts,
        CancellationToken cancellationToken)
    {
        if (!drafts.IsEnabled)
        {
            return TypedResults.NotFound();
        }

        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var organization = await PublicOrganizations.FindAsync(organizationSlug, db, tenant, cancellationToken);
        var (job, gone) = await PublicApplicationEndpoints.FindOpenJobAsync(organizationSlug, jobId, db, tenant, cancellationToken);
        if (gone is not null)
        {
            return gone;
        }

        if (organization is null || job is null)
        {
            return TypedResults.NotFound();
        }

        if (AnswerProblems(job, request.Answers) is { } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var saved = await drafts.CreateAsync(db, organization, job, request.Candidate.Email, Content(request), cancellationToken);
        if (saved is not { } created)
        {
            return TypedResults.Problem(
                title: "Too many saved drafts",
                detail: "This address has been sent several links to finish an application today. Use one of them.",
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        return TypedResults.Created((string?)null, new PublicApplicationDraftReceipt(created.Token, created.Draft.ExpiresAt));
    }

    private static async Task<Results<Ok<PublicApplicationDraft>, NotFound, ProblemHttpResult>> GetDraft(
        string organizationSlug,
        string token,
        ScreenHireDbContext db,
        TenantContext tenant,
        ApplicationDrafts drafts,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var found = await FindAsync(organizationSlug, token, db, tenant, drafts, tracked: false, cancellationToken);
        if (found is not { } draft)
        {
            return TypedResults.NotFound();
        }

        var job = await db.Jobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == draft.JobId, cancellationToken);
        if (job is not { Status: JobStatus.Open } || draft.IsExpired(time.GetUtcNow()))
        {
            return Gone();
        }

        return TypedResults.Ok(new PublicApplicationDraft(
            job.Slug, draft.FirstName, draft.LastName, draft.Email, draft.Phone, draft.Answers, draft.AccommodationRequested,
            draft.AiUseDeclaration, draft.AiUseDetails, draft.ExpiresAt));
    }

    // Saves the draft its link names again: the details and answers, never the email, which a different draft holds.
    private static async Task<Results<Ok<PublicApplicationDraftReceipt>, NotFound, ValidationProblem, ProblemHttpResult>> SaveDraft(
        string organizationSlug,
        string token,
        PublicApplicationDraftRequest request,
        IValidator<PublicApplicationDraftRequest> validator,
        ScreenHireDbContext db,
        TenantContext tenant,
        ApplicationDrafts drafts,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var found = await FindAsync(organizationSlug, token, db, tenant, drafts, tracked: true, cancellationToken);
        if (found is not { } draft)
        {
            return TypedResults.NotFound();
        }

        var now = time.GetUtcNow();
        var job = await db.Jobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == draft.JobId, cancellationToken);
        if (job is not { Status: JobStatus.Open } || draft.IsExpired(now))
        {
            return Gone();
        }

        if (AnswerProblems(job, request.Answers) is { } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        draft.Update(Content(request), now);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(new PublicApplicationDraftReceipt(token, draft.ExpiresAt));
    }

    // The answers' shape against the job's questions, keyed as the submission keys them; nothing is required yet.
    private static Dictionary<string, string[]>? AnswerProblems(Job job, IReadOnlyList<ApplicationAnswer>? answers)
    {
        var errors = ApplicationForm.ValidateDraft(ApplicationForm.QuestionsFor(job.Workflow), answers ?? [])
            .GroupBy(e => $"answers.{e.QuestionId}")
            .ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray());
        return errors.Count > 0 ? errors : null;
    }

    private static ApplicationDraftContent Content(PublicApplicationDraftRequest request) =>
        new(request.Candidate.FirstName, request.Candidate.LastName, request.Candidate.Phone, request.Answers ?? [],
            request.AccommodationRequested, request.AiUseDeclaration, request.AiUseDetails);

    // The slug's organization as the tenant, then the draft the token names, which must be that organization's.
    private static async Task<ApplicationDraft?> FindAsync(
        string organizationSlug, string token, ScreenHireDbContext db, TenantContext tenant, ApplicationDrafts drafts, bool tracked,
        CancellationToken cancellationToken)
    {
        if (drafts.Read(token) is not { } link
            || await PublicOrganizations.FindAsync(organizationSlug, db, tenant, cancellationToken) is not { } organization
            || organization.Id != link.OrganizationId)
        {
            return null;
        }

        var query = tracked ? db.ApplicationDrafts : db.ApplicationDrafts.AsNoTracking();
        return await query.SingleOrDefaultAsync(d => d.Id == link.DraftId, cancellationToken);
    }

    private static ProblemHttpResult Gone() =>
        TypedResults.Problem(
            title: "These saved answers are gone", detail: "The link has expired or the job no longer takes applications.",
            statusCode: StatusCodes.Status410Gone);
}
