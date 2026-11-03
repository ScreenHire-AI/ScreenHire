using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Content;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.Views;
using ScreenHire.Domain.Organizations;
using ScreenHire.Domain.Views;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// Views that people save on the Candidates table and on a job's table (docs/architecture/saved-views.md): the
/// filters and sort, a name, an owner, and whether the team sees it. Session only: a view is a screen preference, not
/// hiring data, so an API key has none and the routes need no hiring permission. Its owner changes it; so does a role
/// with Edit anyone's views. A shared view of a job shows only to members who see that job (ADR-0015). Deleting is soft,
/// so Undo restores the same row. Not in the change history, as notification settings are not.
/// </summary>
internal static class ViewEndpoints
{
    /// <summary>Views one person keeps, so a runaway client cannot fill the table.</summary>
    public const int MaxViewsPerPerson = 200;

    public static IEndpointRouteBuilder MapViewEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/views").WithTags("Views").RequireAuthorization();
        group.MapGet("", ListViews).WithName("ListViews").Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("", CreateView).WithName("CreateView")
            .RequireAntiforgeryToken()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPatch("/{viewId:guid}", UpdateView).WithName("UpdateView")
            .RequireAntiforgeryToken()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapDelete("/{viewId:guid}", DeleteView).WithName("DeleteView")
            .RequireAntiforgeryToken()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/{viewId:guid}/restore", RestoreView).WithName("RestoreView")
            .RequireAntiforgeryToken()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPut("/hidden-built-ins", UpdateHiddenViews).WithName("UpdateHiddenViews")
            .RequireAntiforgeryToken()
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
    }

    // The member's own views and the shared ones, oldest first. A view of a job they cannot see, and a shared view that picks
    // one, are left out: Jobs reads through the job scope.
    private static async Task<Ok<ViewList>> ListViews(ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken) =>
        TypedResults.Ok(await BuildListAsync(db, tenant, cancellationToken));

    private static async Task<ViewList> BuildListAsync(ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken)
    {
        var userId = tenant.UserId;
        var views = await db.SavedFilterViews.AsNoTracking()
            .Where(v => v.DeletedAt == null && (v.IsShared || v.OwnerUserId == userId))
            .Where(v => v.JobId == null || db.Jobs.Any(j => j.Id == v.JobId))
            .OrderBy(v => v.CreatedAt).ThenBy(v => v.Id)
            .ToListAsync(cancellationToken);
        var seenJobs = await SeenJobsAsync(db, views, cancellationToken);
        var permissions = await PermissionsAsync(db, tenant, cancellationToken);
        var preferences = tenant.MemberId is { } memberId
            ? await db.OrganizationMembers.AsNoTracking().Where(m => m.Id == memberId).Select(m => m.Views).SingleOrDefaultAsync(cancellationToken)
            : null;
        return new ViewList(
            [.. views.Where(v => ViewPolicy.MaySee(v, userId, seenJobs)).Select(v => SavedViewSummary.From(v, userId, ViewPolicy.MayChange(v, userId, permissions)))],
            preferences?.HiddenBuiltIns ?? []);
    }

    private static async Task<Results<Created<SavedViewSummary>, ValidationProblem, ProblemHttpResult>> CreateView(
        CreateViewRequest request,
        IValidator<CreateViewRequest> validator,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        ITenantContext tenant,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (tenant.UserId is not { } userId || tenant.OrganizationId is not { } organizationId)
        {
            return TypedResults.Problem(title: "Sign in to save a view", statusCode: StatusCodes.Status401Unauthorized);
        }

        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var db = outbox.DbContext;
        if (request.JobId is { } jobId && !await db.Jobs.AsNoTracking().AnyAsync(j => j.Id == jobId, cancellationToken))
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("jobId", "There is no job with this id."));
        }

        // The jobs a view picks must be jobs its saver sees, so a shared view never carries the id of a job they cannot.
        var jobIds = request.Filters.JobIds.Distinct().ToList();
        if (jobIds.Count > 0 && await db.Jobs.AsNoTracking().CountAsync(j => jobIds.Contains(j.Id), cancellationToken) != jobIds.Count)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("filters.jobIds", "One of these jobs does not exist."));
        }

        if (await db.SavedFilterViews.CountAsync(v => v.OwnerUserId == userId && v.DeletedAt == null, cancellationToken) >= MaxViewsPerPerson)
        {
            return TypedResults.Problem(title: "Too many views", detail: "Delete a view before saving another.", statusCode: StatusCodes.Status409Conflict);
        }

        var name = request.Name.Trim();
        if (await NameTakenAsync(db, userId, request.Scope, request.JobId, name, except: null, cancellationToken))
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("name", "You already have a view with this name here."));
        }

        var now = time.GetUtcNow();
        var view = SavedFilterView.Create(organizationId, userId, name, request.Scope, request.JobId, request.Shared, request.Filters.ToDefinition(), now);
        db.SavedFilterViews.Add(view);
        await outbox.PublishAsync(new ViewsChanged(organizationId, view.Id, now, userId));
        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("name", "You already have a view with this name here."));
        }

        return TypedResults.Created($"/v1/views/{view.Id}", SavedViewSummary.From(view, userId, canEdit: true));
    }

    private static async Task<Results<Ok<SavedViewSummary>, NotFound, ValidationProblem, ProblemHttpResult>> UpdateView(
        Guid viewId,
        UpdateViewRequest request,
        IValidator<UpdateViewRequest> validator,
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
        var (view, refusal) = await FindAsync(db, tenant, viewId, includeDeleted: false, cancellationToken);
        if (refusal is not null)
        {
            return refusal;
        }

        if (view is null)
        {
            return TypedResults.NotFound();
        }

        var now = time.GetUtcNow();
        var name = request.Name?.Trim();
        if (name is not null && name != view.Name
            && await NameTakenAsync(db, view.OwnerUserId, view.Scope, view.JobId, name, except: view.Id, cancellationToken))
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("name", "There is already a view with this name here."));
        }

        var changed = name is not null && view.Rename(name, now);
        if (request.Shared is { } shared)
        {
            changed |= view.SetShared(shared, now);
        }

        if (changed)
        {
            await outbox.PublishAsync(new ViewsChanged(view.OrganizationId, view.Id, now, tenant.UserId));
            try
            {
                await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (exception.IsUniqueViolation())
            {
                return TypedResults.ValidationProblem(ValidationExtensions.Problem("name", "There is already a view with this name here."));
            }
        }

        return TypedResults.Ok(SavedViewSummary.From(view, tenant.UserId, canEdit: true));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteView(
        Guid viewId, IDbContextOutbox<ScreenHireDbContext> outbox, ITenantContext tenant, TimeProvider time, CancellationToken cancellationToken)
    {
        var db = outbox.DbContext;
        var (view, refusal) = await FindAsync(db, tenant, viewId, includeDeleted: false, cancellationToken);
        if (refusal is not null)
        {
            return refusal;
        }

        if (view is null)
        {
            return TypedResults.NotFound();
        }

        var now = time.GetUtcNow();
        view.Delete(now);
        await outbox.PublishAsync(new ViewsChanged(view.OrganizationId, view.Id, now, tenant.UserId));
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);

        // Views deleted long enough ago to be past Undo go for good. Views are not a change-tracked kind, so a bulk delete
        // is fine (CLAUDE.md, change history).
        var cutoff = now - SavedFilterView.RestoreWindow;
        await db.SavedFilterViews.Where(v => v.DeletedAt != null && v.DeletedAt < cutoff).ExecuteDeleteAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<SavedViewSummary>, NotFound, ProblemHttpResult>> RestoreView(
        Guid viewId, IDbContextOutbox<ScreenHireDbContext> outbox, ITenantContext tenant, TimeProvider time, CancellationToken cancellationToken)
    {
        var db = outbox.DbContext;
        var (view, refusal) = await FindAsync(db, tenant, viewId, includeDeleted: true, cancellationToken);
        if (refusal is not null)
        {
            return refusal;
        }

        // Past Undo the view is gone, whether or not a later delete has purged the row yet.
        var now = time.GetUtcNow();
        if (view is null || view.IsPastUndo(now))
        {
            return TypedResults.NotFound();
        }

        // The name may have been taken while the view was deleted; restoring then would clash, so it stays deleted.
        if (view.DeletedAt is not null
            && await NameTakenAsync(db, view.OwnerUserId, view.Scope, view.JobId, view.Name, except: view.Id, cancellationToken))
        {
            return NameTakenOnRestore();
        }

        if (view.Restore(now))
        {
            await outbox.PublishAsync(new ViewsChanged(view.OrganizationId, view.Id, now, tenant.UserId));
            try
            {
                await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (exception.IsUniqueViolation())
            {
                return NameTakenOnRestore();
            }
        }

        return TypedResults.Ok(SavedViewSummary.From(view, tenant.UserId, canEdit: true));
    }

    private static ProblemHttpResult NameTakenOnRestore() => TypedResults.Problem(
        title: "The name is taken",
        detail: "Another view now has this name, so this one could not be brought back.",
        statusCode: StatusCodes.Status409Conflict);

    private static async Task<Results<Ok<ViewList>, NotFound, ValidationProblem>> UpdateHiddenViews(
        UpdateHiddenViewsRequest request,
        IValidator<UpdateHiddenViewsRequest> validator,
        ScreenHireDbContext db,
        ITenantContext tenant,
        CancellationToken cancellationToken)
    {
        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var member = await db.OrganizationMembers.SingleOrDefaultAsync(m => m.Id == tenant.MemberId, cancellationToken);
        if (member is null)
        {
            return TypedResults.NotFound();
        }

        if (member.SetViews(new ViewPreferences { HiddenBuiltIns = request.HiddenBuiltIns }))
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.Ok(await BuildListAsync(db, tenant, cancellationToken));
    }

    // The tracked view, when the member may change it. Another member's private view, and a view of a job they cannot see or
    // one that picks such a job, are not found. A shared view they may not change is refused, naming nobody.
    private static async Task<(SavedFilterView? View, ProblemHttpResult? Refusal)> FindAsync(
        ScreenHireDbContext db, ITenantContext tenant, Guid viewId, bool includeDeleted, CancellationToken cancellationToken)
    {
        var userId = tenant.UserId;
        var view = await db.SavedFilterViews
            .Where(v => v.Id == viewId && (includeDeleted || v.DeletedAt == null) && (v.IsShared || v.OwnerUserId == userId))
            .Where(v => v.JobId == null || db.Jobs.Any(j => j.Id == v.JobId))
            .SingleOrDefaultAsync(cancellationToken);
        if (view is null || !ViewPolicy.MaySee(view, userId, await SeenJobsAsync(db, [view], cancellationToken)))
        {
            return (null, null);
        }

        var permissions = await PermissionsAsync(db, tenant, cancellationToken);
        return ViewPolicy.MayChange(view, userId, permissions)
            ? (view, null)
            : (null, TypedResults.Problem(
                title: "Not your view",
                detail: "Only the person who saved a view, or a role that edits anyone's views, can change it.",
                statusCode: StatusCodes.Status403Forbidden));
    }

    // Which of the jobs these views pick the member sees. The picks are in the view's JSON, so the check runs here rather than
    // in the query. Jobs reads through the job scope.
    private static async Task<IReadOnlySet<Guid>> SeenJobsAsync(ScreenHireDbContext db, IEnumerable<SavedFilterView> views, CancellationToken cancellationToken)
    {
        var picked = views.SelectMany(v => v.Definition.JobIds).Distinct().ToList();
        return picked.Count == 0
            ? new HashSet<Guid>()
            : await db.Jobs.AsNoTracking().Where(j => picked.Contains(j.Id)).Select(j => j.Id).ToHashSetAsync(cancellationToken);
    }

    private static async Task<IReadOnlySet<Permission>?> PermissionsAsync(
        ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken) =>
        tenant is { UserId: { } userId, OrganizationId: { } organizationId }
            ? (await RoleAccess.ForMemberAsync(db, organizationId, userId, cancellationToken))?.Permissions
            : null;

    // Names compare without regard to case, for one person's views of one table. This check gives the friendly answer; the
    // unique index ux_saved_filter_views_name settles two saves at the same moment.
    private static Task<bool> NameTakenAsync(
        ScreenHireDbContext db, Guid ownerUserId, ViewScope scope, Guid? jobId, string name, Guid? except, CancellationToken cancellationToken)
    {
        var exactly = Like.Exactly(name);
        return db.SavedFilterViews.AsNoTracking().AnyAsync(
            v => v.OwnerUserId == ownerUserId && v.Scope == scope && v.JobId == jobId && v.DeletedAt == null && v.Id != except
                 && EF.Functions.ILike(v.Name, exactly, Like.Escape),
            cancellationToken);
    }
}
