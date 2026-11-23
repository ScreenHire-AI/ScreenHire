using ScreenHire.Domain.Automations;
using ScreenHire.Domain.Hiring;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// One application's decision record, for the team answering a candidate's access, correction or review request
/// (docs/architecture/data-requests.md, Decision record): what the application was, everything that happened to it, what
/// the automations did, and its verification with every reviewer action. It is the team's copy. What goes on to the
/// candidate is the organization's decision, and nothing here sends anything.
/// </summary>
/// <remarks>
/// <c>verification</c> is the same record <c>GET /v1/applications/{id}/verification</c> returns: categorical levels,
/// reasons, evidence and counter-evidence, and the review log, with no score or percentage anywhere (ADR-0004).
/// <c>activity</c> is the application's timeline as <c>GET /v1/applications/{id}/timeline</c> returns it, oldest first.
/// <c>stages</c> names every stage id the record mentions, as the job names it now or named it last.
/// <c>privacyNoticeVersion</c> is the careers notice the candidate saw when they applied, null for an application added
/// by hand. <c>accommodationRequested</c> is whether they turned on the assistive technology switch, which turns
/// form-use checks off and is never held against them.
/// </remarks>
public sealed record ApplicationDecisionRecord(
    Guid ApplicationId,
    Guid JobId,
    string JobTitle,
    Guid CandidateId,
    string CandidateName,
    DateTimeOffset SubmittedAt,
    ApplicationStage CurrentStage,
    string? PrivacyNoticeVersion,
    bool AccommodationRequested,
    IReadOnlyList<DecisionRecordStage> Stages,
    IReadOnlyList<ApplicationTimelineEntry> Activity,
    IReadOnlyList<DecisionRecordAutomationRun> AutomationRuns,
    ApplicationVerification Verification,
    DateTimeOffset GeneratedAt);

/// <summary>A stage id the decision record mentions, and its name.</summary>
public sealed record DecisionRecordStage(string Id, string Name);

/// <summary>
/// One automation that was evaluated for the application: its name and workflow version when it ran, what triggered it,
/// how it ended and what each action did. The facts its conditions saw are on <c>GET /v1/applications/{id}/automation-runs</c>.
/// </summary>
public sealed record DecisionRecordAutomationRun(
    Guid Id,
    string AutomationName,
    int WorkflowVersion,
    TriggerType Trigger,
    AutomationRunStatus Status,
    string? Detail,
    IReadOnlyList<AutomationActionEntry> Actions,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FinishedAt);
