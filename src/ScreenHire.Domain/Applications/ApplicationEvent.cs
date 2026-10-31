using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Applications;

/// <summary>
/// One entry of an application's timeline. Append-only: the database lets the application role insert and read these
/// rows but never change or delete one, so the timeline is a record of what happened (workflow-engine.md §6).
/// </summary>
public sealed class ApplicationEvent : IApplicationScoped
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid ApplicationId { get; init; }

    public ApplicationEventType Type { get; init; }

    public required ApplicationEventData Data { get; init; }

    /// <summary>
    /// The signed-in user who caused it, or null for the candidate, the system or an API key (<see cref="ApplicationEventData.ApiKeyId"/>).
    /// </summary>
    public Guid? ActorUserId { get; init; }

    public DateTimeOffset OccurredAt { get; init; }
}

public enum ApplicationEventType
{
    Submitted,
    StageChanged,
    TagAdded,

    /// <summary>The verification engine assessed the application, or reassessed it with a different result.</summary>
    VerificationAssessed,

    /// <summary>A person or an API key took a tag off the application.</summary>
    TagRemoved,

    /// <summary>A note from the hiring team.</summary>
    NoteAdded,

    /// <summary>A reviewer submitted a stage's scorecard; it raises the ReviewerDecision trigger.</summary>
    ScorecardSubmitted,

    /// <summary>An email went to the candidate: sent by a person, or by an automation.</summary>
    EmailSent,

    /// <summary>A SendQuestionnaire action emailed the candidate a link to a follow-up questionnaire, or the same link again.</summary>
    QuestionnaireSent,

    /// <summary>The candidate answered a follow-up questionnaire; it raises the QuestionnaireCompleted trigger.</summary>
    QuestionnaireCompleted,

    /// <summary>A note's author changed its text. The note keeps only its latest text (<see cref="ApplicationNote"/>).</summary>
    NoteEdited,

    /// <summary>A note was deleted, its text with it: only who deleted it and when remain.</summary>
    NoteDeleted,

    /// <summary>A person or an API key gave the hiring team a task about the application. Automations' tasks show as their runs.</summary>
    TaskCreated,

    /// <summary>A person kept the candidate in mind: they left the job without a rejection and joined the talent pool.</summary>
    KeptInMind,

    /// <summary>The candidate, kept in mind from this application, left the talent pool; <c>poolExit</c> says why.</summary>
    LeftTalentPool,

    /// <summary>A person invited the candidate, kept in mind from this application, to apply for another job.</summary>
    InvitedToApply,
}

/// <summary>
/// An event's details. One flat record for every event type, with what does not apply left null, so the timeline API
/// and the JSONB column stay one simple shape. Add members as optional.
/// </summary>
/// <remarks>
/// <c>automationRunId</c>: The automation run that made the change; null when a person or the candidate did. The engine
/// follows it to find which automations led to a move (depth limit and cycle detection).
/// <c>authenticityLevel</c>, <c>aiAssistanceLevel</c>, <c>rulesetVersion</c>: For <see cref="ApplicationEventType.VerificationAssessed"/>,
/// the levels as they stood after the assessment; automation conditions read them as verification facts.
/// <c>apiKeyId</c>: The organization API key that made the change; null when a person, the candidate or the system did.
/// <c>scorecardId</c>, <c>stageId</c>, <c>recommendation</c>: For <see cref="ApplicationEventType.ScorecardSubmitted"/>, the
/// scorecard, the stage it rates and its overall recommendation, which a ReviewerDecision automation reads as a fact.
/// <c>emailTemplateId</c>, <c>emailSubject</c>, <c>emailId</c>: For <see cref="ApplicationEventType.EmailSent"/>, the
/// template it started from (null for a blank email), the subject as the candidate received it, and the copy of the email
/// as it went (<see cref="ApplicationEmail"/>), which holds the body. Emails sent before copies were kept have no
/// <c>emailId</c>; the body is never kept here.
/// <c>questionnaireId</c>, <c>questionnaireRequestId</c>: For <see cref="ApplicationEventType.QuestionnaireSent"/> and
/// <see cref="ApplicationEventType.QuestionnaireCompleted"/>, the follow-up questionnaire and the request that carries
/// its link and, once completed, its answers. The answers are never kept here.
/// <c>noteId</c>: For <see cref="ApplicationEventType.NoteAdded"/>, <see cref="ApplicationEventType.NoteEdited"/> and
/// <see cref="ApplicationEventType.NoteDeleted"/>, the note (<see cref="ApplicationNote"/>), which holds the text. A
/// note's text is never kept here; <c>note</c> is a stage move's note, or a created task's.
/// <c>taskId</c>: For <see cref="ApplicationEventType.TaskCreated"/>, the task (<c>ApplicationTask</c>).
/// <c>poolExit</c>: For <see cref="ApplicationEventType.LeftTalentPool"/>, why (<see cref="TalentPoolExit"/>).
/// <c>otherJobId</c>, <c>otherApplicationId</c>: For <see cref="ApplicationEventType.InvitedToApply"/>, the job the
/// candidate was invited to; for <see cref="ApplicationEventType.LeftTalentPool"/>, the job and application they were
/// added to, or applied with. A <see cref="ApplicationEventType.KeptInMind"/> entry's <c>note</c> says why the team kept
/// them, and is the talent pool's only copy of it.
/// </remarks>
public sealed record ApplicationEventData(
    string? FromStageId = null,
    string? ToStageId = null,
    string? Note = null,
    string? Tag = null,
    Guid? AutomationRunId = null,
    string? AuthenticityLevel = null,
    string? AiAssistanceLevel = null,
    string? RulesetVersion = null,
    Guid? ApiKeyId = null,
    Guid? ScorecardId = null,
    string? StageId = null,
    string? Recommendation = null,
    string? EmailTemplateId = null,
    string? EmailSubject = null,
    string? QuestionnaireId = null,
    Guid? QuestionnaireRequestId = null,
    Guid? NoteId = null,
    Guid? EmailId = null,
    Guid? TaskId = null,
    string? PoolExit = null,
    Guid? OtherJobId = null,
    Guid? OtherApplicationId = null);
