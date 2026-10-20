using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Applications;

/// <summary>
/// A candidate's application to one job, and where it is in that job's workflow. (Named <c>JobApplication</c> because
/// <c>ScreenHire.Application</c> is a namespace.) Every change appends an <see cref="ApplicationEvent"/>; the
/// application row holds only the current state.
/// </summary>
/// <remarks>
/// Applications only move when a person moves them, or later when an automation the workflow validator accepted moves
/// them. Nothing here rejects anyone on its own (CLAUDE.md non-negotiable 2).
/// </remarks>
public sealed class JobApplication : ITenantOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid JobId { get; init; }

    public Guid CandidateId { get; init; }

    /// <summary>A stage id from the job's workflow.</summary>
    public string CurrentStageId { get; private set; } = "";

    /// <summary>The job's workflow version when the application arrived (workflow-engine.md §3).</summary>
    public int WorkflowVersion { get; init; }

    /// <summary>The candidate asked for an accommodation. It switches timing and telemetry facts off and is never held against anyone.</summary>
    public bool AccommodationRequested { get; init; }

    /// <summary>What the candidate said about using AI tools, under the job's AI-use policy.</summary>
    public AiUseDeclaration AiUseDeclaration { get; init; }

    /// <summary>The candidate's own words about their AI use, if they gave any.</summary>
    public string? AiUseDetails { get; init; }

    public DateTimeOffset SubmittedAt { get; init; }

    public DateTimeOffset StageEnteredAt { get; private set; }

    /// <summary>Labels automations and people put on the application; conditions read them as <c>application.tags</c>.</summary>
    public List<string> Tags { get; private set; } = [];

    /// <summary>
    /// A new application in the job's first stage, plus the timeline's first event. Closed jobs take no applications.
    /// </summary>
    public static (JobApplication Application, ApplicationEvent Submitted) Submit(
        Job job, Candidate candidate, bool accommodationRequested, AiUseDeclaration aiUseDeclaration, string? aiUseDetails,
        DateTimeOffset now, Guid? actorUserId)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(candidate);
        if (job.Status == JobStatus.Closed)
        {
            throw new InvalidOperationException("A closed job takes no applications.");
        }

        if (job.OrganizationId != candidate.OrganizationId)
        {
            throw new InvalidOperationException("The candidate belongs to another organization.");
        }

        var firstStage = job.Workflow.Stages[0];
        var application = new JobApplication
        {
            OrganizationId = job.OrganizationId,
            JobId = job.Id,
            CandidateId = candidate.Id,
            CurrentStageId = firstStage.Id,
            WorkflowVersion = job.WorkflowVersion,
            AccommodationRequested = accommodationRequested,
            AiUseDeclaration = aiUseDeclaration,
            AiUseDetails = string.IsNullOrWhiteSpace(aiUseDetails) ? null : aiUseDetails.Trim(),
            SubmittedAt = now,
            StageEnteredAt = now,
        };

        return (application, application.NewEvent(ApplicationEventType.Submitted, new ApplicationEventData(ToStageId: firstStage.Id), now, actorUserId));
    }

    /// <summary>
    /// Moves the application to <paramref name="target"/>, a stage of its job's workflow, and returns the timeline event.
    /// Moving to the stage it is already in changes nothing and returns null.
    /// </summary>
    public ApplicationEvent? MoveTo(WorkflowStage target, DateTimeOffset now, Guid? actorUserId, string? note, Guid? automationRunId = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Id == CurrentStageId)
        {
            return null;
        }

        var from = CurrentStageId;
        CurrentStageId = target.Id;
        StageEnteredAt = now;
        return NewEvent(
            ApplicationEventType.StageChanged,
            new ApplicationEventData(from, target.Id, string.IsNullOrWhiteSpace(note) ? null : note.Trim(), AutomationRunId: automationRunId),
            now,
            actorUserId);
    }

    /// <summary>
    /// Adds <paramref name="tag"/> (trimmed; tags compare ignoring case) and returns the timeline event. A tag the
    /// application already has changes nothing and returns null.
    /// </summary>
    public ApplicationEvent? AddTag(string tag, DateTimeOffset now, Guid? actorUserId, Guid? automationRunId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        var trimmed = tag.Trim();
        if (Tags.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        Tags = [.. Tags, trimmed];
        return NewEvent(ApplicationEventType.TagAdded, new ApplicationEventData(Tag: trimmed, AutomationRunId: automationRunId), now, actorUserId);
    }

    private ApplicationEvent NewEvent(ApplicationEventType type, ApplicationEventData data, DateTimeOffset now, Guid? actorUserId) =>
        new()
        {
            OrganizationId = OrganizationId,
            ApplicationId = Id,
            Type = type,
            Data = data,
            ActorUserId = actorUserId,
            OccurredAt = now,
        };
}

/// <summary>The candidate's answer to "did you use AI tools to apply?".</summary>
public enum AiUseDeclaration
{
    /// <summary>The candidate was not asked, or did not answer.</summary>
    NotProvided,
    DidNotUse,
    Used,
}
