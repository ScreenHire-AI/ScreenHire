using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Api.Hiring;
using ScreenHire.Application.Jobs;
using ScreenHire.Application.Tags;
using ScreenHire.Application.Templates;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// The organization's shared tags (docs/architecture/tags.md): the list the add field suggests from and Settings, Tags
/// shows, and that page's rename, merge and delete. Reading is hiring data, so an API key may read it. Changing a tag
/// reaches every job, so it needs Edit settings and a member who sees all jobs: someone limited to their own jobs would
/// change only the applications they can see.
/// </summary>
internal static class TagEndpoints
{
    public static IEndpointRouteBuilder MapTagEndpoints(this IEndpointRouteBuilder app)
    {
        var read = app.MapGroup("/v1/tags").WithTags("Tags").AllowApiKeys();
        read.MapGet("/", ListTags).WithName("ListTags").Produces(StatusCodes.Status401Unauthorized);

        var change = app.MapGroup("/v1/tags").WithTags("Tags").RequireAuthorization();
        change.MapPost("/rename", RenameTag).WithName("RenameTag")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EditSettings)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        change.MapPost("/merge", MergeTag).WithName("MergeTag")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EditSettings)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        change.MapDelete("/", DeleteTag).WithName("DeleteTag")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EditSettings)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
    }

    private static async Task<Ok<TagList>> ListTags(ScreenHireDbContext db, CancellationToken cancellationToken) =>
        TypedResults.Ok(new TagList(await OrganizationTags.ListAsync(db, cancellationToken)));

    // A rename to a name another tag already has would merge them without saying so, so it is refused: Merge says it.
    private static async Task<Results<Ok<TagChangeResult>, NotFound, ValidationProblem, ProblemHttpResult>> RenameTag(
        RenameTagRequest request, IDbContextOutbox<ScreenHireDbContext> outbox, ITenantContext tenant, TimeProvider time, CancellationToken cancellationToken)
    {
        if (!tenant.SeesAllJobs)
        {
            return HiringAuthorization.NeedsAllJobs(Permission.EditSettings);
        }

        if (NameProblem("newName", request.NewName) is { } problem)
        {
            return TypedResults.ValidationProblem(problem);
        }

        var tags = await OrganizationTags.ListAsync(outbox.DbContext, cancellationToken);
        if (Find(tags, request.Tag) is not { } from)
        {
            return TypedResults.NotFound();
        }

        var to = request.NewName.Trim();
        if (to == from.Name)
        {
            return TypedResults.Ok(new TagChangeResult(0, 0, 0, await OrganizationTags.PublishedNamingAsync(outbox.DbContext, from.Name, cancellationToken)));
        }

        if (Find(tags, to) is { } other && other != from)
        {
            return TypedResults.Problem(
                title: "Already a tag", detail: $"\"{other.Name}\" is already a tag. Merge into it to combine the two.", statusCode: StatusCodes.Status409Conflict);
        }

        return await ChangeAsync(outbox, tenant, time, from.Name, to, cancellationToken);
    }

    private static async Task<Results<Ok<TagChangeResult>, NotFound, ValidationProblem, ProblemHttpResult>> MergeTag(
        MergeTagRequest request, IDbContextOutbox<ScreenHireDbContext> outbox, ITenantContext tenant, TimeProvider time, CancellationToken cancellationToken)
    {
        if (!tenant.SeesAllJobs)
        {
            return HiringAuthorization.NeedsAllJobs(Permission.EditSettings);
        }

        var tags = await OrganizationTags.ListAsync(outbox.DbContext, cancellationToken);
        if (Find(tags, request.Tag) is not { } from)
        {
            return TypedResults.NotFound();
        }

        if (Find(tags, request.Into) is not { } into)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("into", "Choose one of the organization's tags to merge into."));
        }

        if (into == from)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("into", "Choose another tag: a tag cannot merge into itself."));
        }

        return await ChangeAsync(outbox, tenant, time, from.Name, into.Name, cancellationToken);
    }

    // Takes the tag off every application. Automations that name it keep naming it: an Add a tag step would add it again,
    // and the Tags page keeps listing the tag while a template or a job names it.
    private static async Task<Results<Ok<TagChangeResult>, NotFound, ValidationProblem, ProblemHttpResult>> DeleteTag(
        string? tag, IDbContextOutbox<ScreenHireDbContext> outbox, ITenantContext tenant, CancellationToken cancellationToken)
    {
        if (!tenant.SeesAllJobs)
        {
            return HiringAuthorization.NeedsAllJobs(Permission.EditSettings);
        }

        if (NameProblem("tag", tag) is { } problem)
        {
            return TypedResults.ValidationProblem(problem);
        }

        if (Find(await OrganizationTags.ListAsync(outbox.DbContext, cancellationToken), tag!) is not { } found)
        {
            return TypedResults.NotFound();
        }

        await outbox.PublishAsync(new RetagApplications(tenant.OrganizationId!.Value, found.Name, null, tenant.UserId));
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        return TypedResults.Ok(new TagChangeResult(0, 0, 0, await OrganizationTags.PublishedNamingAsync(outbox.DbContext, found.Name, cancellationToken)));
    }

    /// <summary>
    /// A rename or a merge: the automations of every job that is not closed and of every draft that is not archived, and
    /// the saved views, in this save, through the change tracker, so each job's and template's history records it; then
    /// the applications, a batch at a time in the worker (<see cref="RetagApplications"/>). Published versions never change.
    /// </summary>
    private static async Task<Results<Ok<TagChangeResult>, NotFound, ValidationProblem, ProblemHttpResult>> ChangeAsync(
        IDbContextOutbox<ScreenHireDbContext> outbox, ITenantContext tenant, TimeProvider time, string from, string to, CancellationToken cancellationToken)
    {
        var db = outbox.DbContext;
        var now = time.GetUtcNow();
        var organizationId = tenant.OrganizationId!.Value;

        var jobs = 0;
        foreach (var job in await db.Jobs.Where(j => j.Status != JobStatus.Closed).ToListAsync(cancellationToken))
        {
            if (WorkflowTags.Rename(job.Workflow, from, to) is { } renamed)
            {
                job.ReviseWorkflow(renamed, now);
                await outbox.PublishAsync(new JobWorkflowChanged(job.OrganizationId, job.Id, job.WorkflowVersion, now));
                jobs++;
            }
        }

        var drafts = 0;
        var drafted = await db.HiringTemplateVersions
            .Where(v => v.OrganizationId != null && v.Status == TemplateVersionStatus.Draft
                && db.HiringTemplates.Any(t => t.Id == v.TemplateId && t.ArchivedAt == null))
            .ToListAsync(cancellationToken);
        foreach (var draft in drafted)
        {
            if (WorkflowTags.Rename(draft.Definition.Workflow, from, to) is { } renamed)
            {
                draft.Revise(draft.Definition with { Workflow = renamed });
                await outbox.PublishAsync(new TemplateChanged(organizationId, TemplateKind.HiringTemplate, draft.TemplateId.ToString("D"), now, tenant.UserId));
                drafts++;
            }
        }

        var views = 0;
        foreach (var view in await db.SavedFilterViews.ToListAsync(cancellationToken))
        {
            if (view.RenameTag(from, to))
            {
                views++;
            }
        }

        await outbox.PublishAsync(new RetagApplications(organizationId, from, to, tenant.UserId));
        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TypedResults.Problem(
                "Someone else changed a job or a template at the same moment; try again.", statusCode: StatusCodes.Status409Conflict);
        }

        return TypedResults.Ok(new TagChangeResult(jobs, drafts, views, await OrganizationTags.PublishedNamingAsync(db, from, cancellationToken)));
    }

    private static TagSummary? Find(IReadOnlyList<TagSummary> tags, string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : tags.FirstOrDefault(t => string.Equals(t.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    private static Dictionary<string, string[]>? NameProblem(string field, string? name) =>
        string.IsNullOrWhiteSpace(name) || name.Trim().Length > JobApplication.MaxTagLength
            ? ValidationExtensions.Problem(field, $"Name the tag, in at most {JobApplication.MaxTagLength} characters.")
            : null;
}
