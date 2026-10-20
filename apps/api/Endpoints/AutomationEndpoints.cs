using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.Automations;
using ScreenHire.Domain.Automations;
using ScreenHire.Domain.Hiring;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// What the workflow engine did (workflow-engine.md §3): each application's automation runs (the audit record), the
/// organization's automation issues, the tasks automations created, and running a Manual automation. Tenant-scoped:
/// another organization's application is a 404, and the tenant filter and row-level security scope every list.
/// </summary>
internal static class AutomationEndpoints
{
    private const int IssueLimit = 100;

    public static IEndpointRouteBuilder MapAutomationEndpoints(this IEndpointRouteBuilder app)
    {
        var applications = app.MapGroup("/v1/applications/{applicationId:guid}").WithTags("Automations").RequireAuthorization();
        applications.MapGet("/automation-runs", ListAutomationRuns).WithName("ListAutomationRuns").Produces(StatusCodes.Status401Unauthorized);
        applications.MapGet("/tasks", ListApplicationTasks).WithName("ListApplicationTasks").Produces(StatusCodes.Status401Unauthorized);
        applications.MapPost("/automations/{automationId}/run", RunManualAutomation).WithName("RunManualAutomation")
            .RequireAntiforgeryToken()
            .RequireHiringRole()
            .Produces(StatusCodes.Status401Unauthorized);

        app.MapGet("/v1/automation-issues", ListAutomationIssues).WithName("ListAutomationIssues").WithTags("Automations")
            .RequireAuthorization()
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
    }

    private static async Task<Results<Ok<List<AutomationRunEntry>>, NotFound>> ListAutomationRuns(
        Guid applicationId, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.Applications.AnyAsync(a => a.Id == applicationId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var runs = await db.AutomationRuns.AsNoTracking()
            .Where(r => r.ApplicationId == applicationId)
            .OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);
        var runIds = runs.Select(r => r.Id).ToList();
        var actions = await db.AutomationActionRuns.AsNoTracking().Where(a => runIds.Contains(a.RunId)).ToListAsync(cancellationToken);
        var byRun = actions.ToLookup(a => a.RunId);
        return TypedResults.Ok(runs.Select(r => r.ToEntry(byRun[r.Id])).ToList());
    }

    // Blocked and failed runs, newest first: what the hiring team should look at.
    private static async Task<List<AutomationIssue>> ListAutomationIssues(ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var runs = await db.AutomationRuns.AsNoTracking()
            .Where(r => r.Status == AutomationRunStatus.Blocked || r.Status == AutomationRunStatus.Failed)
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Take(IssueLimit)
            .ToListAsync(cancellationToken);
        return [.. runs.Select(r => r.ToIssue())];
    }

    private static async Task<Results<Ok<List<ApplicationTaskEntry>>, NotFound>> ListApplicationTasks(
        Guid applicationId, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.Applications.AnyAsync(a => a.Id == applicationId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var tasks = await db.ApplicationTasks.AsNoTracking()
            .Where(t => t.ApplicationId == applicationId)
            .OrderBy(t => t.CreatedAt).ThenBy(t => t.Id)
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(tasks.Select(t => t.ToEntry()).ToList());
    }

    // A person runs a Manual automation for one application. The worker evaluates it like any other trigger, conditions
    // and guardrails included; the request id keys the run, and the user who asked is recorded on it.
    private static async Task<Results<Accepted<ManualAutomationQueued>, NotFound, ValidationProblem>> RunManualAutomation(
        Guid applicationId,
        string automationId,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        ITenantContext tenant,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var db = outbox.DbContext;
        var application = await db.Applications.AsNoTracking().SingleOrDefaultAsync(a => a.Id == applicationId, cancellationToken);
        if (application is null)
        {
            return TypedResults.NotFound();
        }

        var job = await db.Jobs.AsNoTracking().SingleAsync(j => j.Id == application.JobId, cancellationToken);
        if (job.Workflow.Automations.FirstOrDefault(a => a.Id == automationId) is not { } automation)
        {
            return TypedResults.NotFound();
        }

        if (automation.Trigger.Type != TriggerType.Manual || !automation.Enabled)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem(
                "automationId", "Only an enabled automation with a Manual trigger can be run by hand."));
        }

        var eventId = Guid.CreateVersion7();
        await outbox.PublishAsync(new RunAutomations(
            application.OrganizationId, application.Id, TriggerType.Manual, null, eventId, time.GetUtcNow(), automation.Id, ActorUserId: tenant.UserId));
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        return TypedResults.Accepted(
            $"/v1/applications/{application.Id}/automation-runs", new ManualAutomationQueued(application.Id, automation.Id, eventId));
    }
}
