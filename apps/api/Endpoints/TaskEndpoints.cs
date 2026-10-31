using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.Automations;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// The tasks automations and people make (AssignReviewer, CreateTask, QueueForRejectionReview, a requested scorecard)
/// across the organization's applications: the dashboard's My tasks. A to-do and a rejection review are ticked off here;
/// a review and a requested scorecard are done when the scorecard is submitted (ADR-0017 decision 11), so these routes
/// refuse them with a 409. Session only, because "mine" needs a person. Tenant-scoped by the query
/// filter and row-level security: another organization's task is a 404. Completing a task, or reopening it with Undo,
/// changes nothing about the application; a rejection review is decided by moving the candidate (workflow-engine.md §2,
/// guardrail 2).
/// </summary>
internal static class TaskEndpoints
{
    // Done tasks pile up, so a list shows the most recent ones. Paging comes with a screen that needs it.
    private const int CompletedLimit = 200;

    public static IEndpointRouteBuilder MapTaskEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/tasks").WithTags("Tasks").RequireAuthorization();
        group.MapGet("/", ListTasks).WithName("ListTasks").Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/{taskId:guid}/complete", CompleteTask).WithName("CompleteTask")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.CompleteTasks)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/{taskId:guid}/reopen", ReopenTask).WithName("ReopenTask")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.CompleteTasks)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
    }

    // Mine is the tasks assigned to the signed-in person, Team the tasks for the whole team (no assignee), and Everyone
    // every task the person can see, teammates' included. Open tasks oldest first, so the longest-waiting is on top;
    // done ones newest first.
    private static async Task<Ok<List<TaskListItem>>> ListTasks(
        ScreenHireDbContext db,
        ITenantContext tenant,
        CancellationToken cancellationToken,
        TaskStatusFilter status = TaskStatusFilter.Open,
        TaskScope scope = TaskScope.Mine)
    {
        var userId = tenant.UserId;
        var tasks = db.ApplicationTasks.AsNoTracking();
        tasks = scope switch
        {
            TaskScope.Mine => tasks.Where(t => t.AssigneeUserId == userId),
            TaskScope.Team => tasks.Where(t => t.AssigneeUserId == null),
            _ => tasks,
        };

        tasks = status == TaskStatusFilter.Open
            ? tasks.Where(t => t.CompletedAt == null).OrderBy(t => t.CreatedAt).ThenBy(t => t.Id)
            : tasks.Where(t => t.CompletedAt != null).OrderByDescending(t => t.CompletedAt).ThenByDescending(t => t.Id).Take(CompletedLimit);
        var rows = await (
            from task in tasks
            join application in db.Applications on task.ApplicationId equals application.Id
            join job in db.Jobs on application.JobId equals job.Id
            join candidate in db.Candidates on application.CandidateId equals candidate.Id
            select new TaskListItem(
                task.Id, task.ApplicationId, application.JobId, candidate.FullName, job.Title, task.Kind, task.Note, task.AssigneeUserId,
                task.AutomationRunId, task.CreatedAt, task.CompletedAt, task.CompletedByUserId, task.ScorecardTemplateId))
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(rows);
    }

    // Anyone who can change hiring data may mark any of the organization's tasks done, the assignee's included: the
    // team shares the work. A task that is done already stays as it was and raises nothing, including one someone else
    // completed between this request's read and its save (CompletedAt is a concurrency token).
    private static async Task<Results<Ok<ApplicationTaskEntry>, NotFound, ProblemHttpResult>> CompleteTask(
        Guid taskId,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        ITenantContext tenant,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var db = outbox.DbContext;
        var task = await db.ApplicationTasks.SingleOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task is null || tenant.UserId is not { } userId)
        {
            return TypedResults.NotFound();
        }

        if (!task.CompletesByHand)
        {
            return DoneWhereTheWorkIs();
        }

        var now = time.GetUtcNow();
        if (task.Complete(userId, now))
        {
            var jobId = await db.Applications.AsNoTracking()
                .Where(a => a.Id == task.ApplicationId).Select(a => a.JobId).SingleAsync(cancellationToken);
            await outbox.PublishAsync(new ApplicationTaskCompleted(task.OrganizationId, task.Id, task.ApplicationId, jobId, now));
            try
            {
                await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // The other completion won; its save carried the event, and this one's rolled back with it.
                var stored = await db.ApplicationTasks.AsNoTracking().SingleAsync(t => t.Id == taskId, cancellationToken);
                return TypedResults.Ok(stored.ToEntry());
            }
        }

        return TypedResults.Ok(task.ToEntry());
    }

    // Undo after ticking a task: anyone who may complete tasks may open one again. A task that is open already stays as
    // it is and raises nothing, including one reopened between this request's read and its save (CompletedAt is a
    // concurrency token, as for completing).
    private static async Task<Results<Ok<ApplicationTaskEntry>, NotFound, ProblemHttpResult>> ReopenTask(
        Guid taskId,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var db = outbox.DbContext;
        var task = await db.ApplicationTasks.SingleOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task is null)
        {
            return TypedResults.NotFound();
        }

        // A submitted scorecard is final, so a review or request it finished stays done.
        if (!task.CompletesByHand)
        {
            return DoneWhereTheWorkIs();
        }

        if (task.Reopen())
        {
            var jobId = await db.Applications.AsNoTracking()
                .Where(a => a.Id == task.ApplicationId).Select(a => a.JobId).SingleAsync(cancellationToken);
            await outbox.PublishAsync(new ApplicationTaskReopened(task.OrganizationId, task.Id, task.ApplicationId, jobId, time.GetUtcNow()));
            try
            {
                await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                var stored = await db.ApplicationTasks.AsNoTracking().SingleAsync(t => t.Id == taskId, cancellationToken);
                return TypedResults.Ok(stored.ToEntry());
            }
        }

        return TypedResults.Ok(task.ToEntry());
    }

    private static ProblemHttpResult DoneWhereTheWorkIs() =>
        TypedResults.Problem(
            title: "Done when its scorecard is submitted",
            detail: "A review or a requested scorecard is done when the scorecard is submitted, not by ticking it.",
            statusCode: StatusCodes.Status409Conflict);
}
