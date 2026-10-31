using ScreenHire.Application.Messaging;

namespace ScreenHire.Application.Applications;

/// <summary>
/// A person or an API key added a note to an application's timeline. <see cref="EventId"/> is the timeline entry; the
/// note's text stays behind the API, so open dashboards refetch the timeline to read it. <see cref="ActorUserId"/> is the
/// member who wrote it, null for an API key.
/// </summary>
public sealed record ApplicationNoteAdded(
    Guid OrganizationId, Guid ApplicationId, Guid JobId, Guid EventId, DateTimeOffset AddedAt, Guid? ActorUserId = null) : IJobEvent;

/// <summary>
/// A person or an API key added a tag to an application or took one off. Ids only: dashboards refetch the
/// application and the lists that filter by tag. An automation's AddTag action raises nothing of its own; its
/// <see cref="Automations.AutomationRunFinished"/> refreshes the same screens. <see cref="ActorUserId"/> is the member,
/// null for an API key.
/// </summary>
public sealed record ApplicationTagsChanged(
    Guid OrganizationId, Guid ApplicationId, Guid JobId, Guid EventId, DateTimeOffset ChangedAt, Guid? ActorUserId = null) : IJobEvent;

/// <summary>
/// A reviewer submitted a stage's scorecard. Ids only: open dashboards refetch the application's scorecards and timeline.
/// The ReviewerDecision automations it fires run in the worker, from the <c>RunAutomations</c> published with it.
/// <see cref="ActorUserId"/> is the reviewer.
/// </summary>
public sealed record ScorecardSubmitted(
    Guid OrganizationId, Guid ApplicationId, Guid JobId, Guid ScorecardId, Guid EventId, DateTimeOffset SubmittedAt,
    Guid? ActorUserId = null) : IJobEvent;

/// <summary>
/// The candidate answered a follow-up questionnaire. Ids only: open dashboards refetch the application, whose answers
/// stay behind the API, and its timeline. The QuestionnaireCompleted automations it fires run in the worker, from the
/// <c>RunAutomations</c> published with it.
/// </summary>
public sealed record QuestionnaireAnswered(
    Guid OrganizationId, Guid ApplicationId, Guid JobId, Guid RequestId, Guid EventId, DateTimeOffset AnsweredAt) : IJobEvent;
