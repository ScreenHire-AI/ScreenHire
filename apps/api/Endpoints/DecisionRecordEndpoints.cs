using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Applications;
using ScreenHire.Api.Configuration;
using ScreenHire.Api.Contracts;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// One application's decision record (docs/architecture/data-requests.md, Decision record), behind
/// <c>ScreenHire:Compliance:DecisionRecord:Enabled</c>, which is off until the owner turns it on. It is built from the
/// reads the dashboard already uses (the timeline, the automation runs and the verification), so it shows a member
/// nothing their own screens do not. It is the session's only: verification output reaches a key solely through the
/// verification read routes, which log every read (ADR-0021).
/// </summary>
internal static class DecisionRecordEndpoints
{
    public static IEndpointRouteBuilder MapDecisionRecordEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/v1/applications/{applicationId:guid}").WithTags("Applications").RequireAuthorization()
            .MapGet("/decision-record", GetDecisionRecord).WithName("GetApplicationDecisionRecord")
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
    }

    private static async Task<Results<Ok<ApplicationDecisionRecord>, NotFound>> GetDecisionRecord(
        Guid applicationId,
        ClaimsPrincipal user,
        ITenantContext tenant,
        ScreenHireDbContext db,
        IOptions<ComplianceOptions> options,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!options.Value.DecisionRecord.Enabled)
        {
            return TypedResults.NotFound();
        }

        var application = await db.Applications.AsNoTracking().FirstOrDefaultAsync(a => a.Id == applicationId, cancellationToken);
        if (application is null)
        {
            return TypedResults.NotFound();
        }

        var job = await db.Jobs.AsNoTracking().FirstAsync(j => j.Id == application.JobId, cancellationToken);
        var candidate = await db.Candidates.AsNoTracking().FirstAsync(c => c.Id == application.CandidateId, cancellationToken);
        var activity = await ApplicationReads.TimelineAsync(user, tenant, db, applicationId, cancellationToken) ?? [];
        var runs = await db.AutomationRuns.AsNoTracking()
            .Where(r => r.ApplicationId == applicationId)
            .OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);
        var runIds = runs.Select(r => r.Id).ToList();
        var actions = (await db.AutomationActionRuns.AsNoTracking().Where(a => runIds.Contains(a.RunId)).ToListAsync(cancellationToken))
            .ToLookup(a => a.RunId);
        var verification = await VerificationReads.LoadAsync(db, applicationId, cancellationToken);

        var stageIds = activity.SelectMany(e => new[] { e.FromStageId, e.ToStageId, e.StageId })
            .Prepend(application.CurrentStageId)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal);
        return TypedResults.Ok(new ApplicationDecisionRecord(
            application.Id,
            job.Id,
            job.Title,
            candidate.Id,
            candidate.FullName,
            application.SubmittedAt,
            application.StageIn(job),
            application.PrivacyNoticeVersion,
            application.AccommodationRequested,
            [.. stageIds.Select(id => new DecisionRecordStage(id, job.StageName(id) ?? id))],
            activity,
            [.. runs.Select(r => r.ToEntry(actions[r.Id])).Select(r => new DecisionRecordAutomationRun(
                r.Id, r.AutomationName, r.WorkflowVersion, r.Trigger, r.Status, r.Detail, r.Actions, r.CreatedAt, r.FinishedAt))],
            verification,
            time.GetUtcNow()));
    }
}
