using System.ComponentModel;
using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;
using ScreenHire.Api.Applications;
using ScreenHire.Api.Contracts;
using ScreenHire.Api.Endpoints;
using ScreenHire.Application.Slack;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.Api.Mcp;

/// <summary>
/// Scorecards: read an application's, and ask a teammate for one. Both call their route's code
/// (<see cref="ScorecardEndpoints"/>, <see cref="ApplicationChanges.RequestScorecardAsync"/>). A key fills in no scorecard:
/// a scorecard is a person's judgement.
/// </summary>
[McpServerToolType]
internal sealed class ScorecardTools(
    ScreenHireDbContext db,
    IDbContextOutbox<ScreenHireDbContext> outbox,
    ITenantContext tenant,
    SlackAccess slack,
    TimeProvider time,
    IValidator<RequestScorecardRequest> requestValidator)
{
    private const string NoApplication = "No application with that id in this organization.";

    [McpServerTool(Name = "get_scorecards", Title = "Read an application's scorecards", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Read the scorecards reviewers gave an application, oldest first: each one's stage, reviewer, recommendation, rating, answers "
        + "and note. They are the hiring team's judgements; verification results are not among them.")]
    public async Task<McpScorecards> GetScorecards(
        [Description("The application's id.")] Guid applicationId,
        CancellationToken cancellationToken = default)
    {
        var result = await ScorecardEndpoints.ListApplicationScorecards(applicationId, tenant, db, cancellationToken);
        var scorecards = McpResults.ValueOf<List<ScorecardEntry>>(result.Result, NoApplication);
        return new(applicationId, [.. scorecards.Select(s => new McpScorecard(
            s.Id, s.StageId, s.ReviewerUserId, s.Recommendation, s.Rating, s.Answers, s.Note, s.SubmittedAt))]);
    }

    [McpServerTool(Name = "request_scorecard", Title = "Ask a teammate for a scorecard", Destructive = false, OpenWorld = false, UseStructuredContent = true)]
    [Authorize(Policy = McpSetup.WritePolicy)]
    [Description("Ask a teammate to fill in a scorecard for an application: a task for them, done when they submit it. scorecardTemplateId names a "
        + "library scorecard, such as a stage's scorecardTemplateId from get_job. The teammate must see the job and be able to fill in scorecards.")]
    public async Task<McpTask> RequestScorecard(
        ClaimsPrincipal user,
        [Description("The application's id.")] Guid applicationId,
        [Description("The teammate's user id, from list_team_members.")] Guid reviewerUserId,
        [Description("The library scorecard to fill in, such as \"interview\".")] string scorecardTemplateId,
        [Description("A note for them, up to 1,000 characters.")] string? note = null,
        CancellationToken cancellationToken = default)
    {
        McpResults.Require(user, Permission.CreateTasks);
        var result = await ApplicationChanges.RequestScorecardAsync(
            applicationId, new RequestScorecardRequest(reviewerUserId, scorecardTemplateId, note), requestValidator, outbox, tenant, user, slack, time,
            cancellationToken);
        return McpResults.ValueOf<ApplicationTaskEntry>(result.Result, NoApplication).ToMcp();
    }
}
