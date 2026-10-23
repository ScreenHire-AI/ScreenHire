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

    /// <summary>Where the application came from.</summary>
    public ApplicationSource Source { get; init; }

    /// <summary>The candidate's answers to the job's questions (<see cref="ApplicationForm"/>), empty ones left out.</summary>
    public IReadOnlyList<ApplicationAnswer> Answers { get; init; } = [];

    /// <summary>
    /// The version of the privacy notice the candidate saw before anything was collected (CLAUDE.md non-negotiable 4),
    /// or null for an application a recruiter added.
    /// </summary>
    public string? PrivacyNoticeVersion { get; init; }

    /// <summary>
    /// The careers form's hidden trap field had a value. An automation signal for the integrity engine
    /// (<c>AUTO_HONEYPOT</c>), recorded and never used to refuse or reject the application.
    /// </summary>
    public bool HoneypotFilled { get; init; }

    /// <summary>The bot challenge's outcome (<c>AUTO_TURNSTILE_FAIL</c>), a signal like <see cref="HoneypotFilled"/>.</summary>
    public BotCheckResult BotCheck { get; init; }

    /// <summary>
    /// A new application in the job's first stage, plus the timeline's first event. Closed jobs take no applications.
    /// </summary>
    /// <remarks>
    /// <paramref name="intake"/> says how the application arrived; leave it null for one a recruiter added by hand.
    /// <paramref name="actorApiKeyId"/> is the API key that added it, if one did.
    /// </remarks>
    public static (JobApplication Application, ApplicationEvent Submitted) Submit(
        Job job, Candidate candidate, bool accommodationRequested, AiUseDeclaration aiUseDeclaration, string? aiUseDetails,
        DateTimeOffset now, Guid? actorUserId, ApplicationIntake? intake = null, Guid? actorApiKeyId = null)
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

        intake ??= ApplicationIntake.Recruiter;
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
            Source = intake.Source,
            Answers = ApplicationForm.Normalize(intake.Answers),
            PrivacyNoticeVersion = intake.PrivacyNoticeVersion,
            HoneypotFilled = intake.HoneypotFilled,
            BotCheck = intake.BotCheck,
        };

        return (application, application.NewEvent(
            ApplicationEventType.Submitted, new ApplicationEventData(ToStageId: firstStage.Id, ApiKeyId: actorApiKeyId), now, actorUserId));
    }

    /// <summary>
    /// Moves the application to <paramref name="target"/>, a stage of its job's workflow, and returns the timeline event.
    /// Moving to the stage it is already in changes nothing and returns null. <paramref name="actorApiKeyId"/> is the API
    /// key that moved it, if one did.
    /// </summary>
    public ApplicationEvent? MoveTo(
        WorkflowStage target, DateTimeOffset now, Guid? actorUserId, string? note, Guid? automationRunId = null, Guid? actorApiKeyId = null)
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
            new ApplicationEventData(
                from, target.Id, string.IsNullOrWhiteSpace(note) ? null : note.Trim(), AutomationRunId: automationRunId, ApiKeyId: actorApiKeyId),
            now,
            actorUserId);
    }

    /// <summary>The longest tag, as the workflow validator allows it in an AddTag action.</summary>
    public const int MaxTagLength = 50;

    /// <summary>The longest note a person can add to the timeline.</summary>
    public const int MaxNoteLength = 2000;

    /// <summary>
    /// Adds <paramref name="tag"/> (trimmed; tags compare ignoring case) and returns the timeline event. A tag the
    /// application already has changes nothing and returns null. <paramref name="actorApiKeyId"/> is the API key that
    /// added it, if one did.
    /// </summary>
    public ApplicationEvent? AddTag(string tag, DateTimeOffset now, Guid? actorUserId, Guid? automationRunId = null, Guid? actorApiKeyId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        var trimmed = tag.Trim();
        if (Tags.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        Tags = [.. Tags, trimmed];
        return NewEvent(
            ApplicationEventType.TagAdded, new ApplicationEventData(Tag: trimmed, AutomationRunId: automationRunId, ApiKeyId: actorApiKeyId), now, actorUserId);
    }

    /// <summary>
    /// Removes <paramref name="tag"/> (compared ignoring case) and returns the timeline event, which names the tag as
    /// the application had it. A tag the application does not have changes nothing and returns null. Only people and
    /// API keys remove tags; no automation action does.
    /// </summary>
    public ApplicationEvent? RemoveTag(string tag, DateTimeOffset now, Guid? actorUserId, Guid? actorApiKeyId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        var trimmed = tag.Trim();
        var existing = Tags.FirstOrDefault(t => string.Equals(t, trimmed, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            return null;
        }

        Tags = [.. Tags.Where(t => t != existing)];
        return NewEvent(ApplicationEventType.TagRemoved, new ApplicationEventData(Tag: existing, ApiKeyId: actorApiKeyId), now, actorUserId);
    }

    /// <summary>
    /// A note from the hiring team, kept on the timeline with its author and time. It changes nothing else about the
    /// application, and no automation reads it.
    /// </summary>
    public ApplicationEvent AddNote(string note, DateTimeOffset now, Guid? actorUserId, Guid? actorApiKeyId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(note);
        return NewEvent(ApplicationEventType.NoteAdded, new ApplicationEventData(Note: note.Trim(), ApiKeyId: actorApiKeyId), now, actorUserId);
    }

    /// <summary>
    /// A reviewer's scorecard for <paramref name="stage"/> and the timeline entry that records it, which raises the
    /// ReviewerDecision trigger. <paramref name="ratings"/> holds ratings by criterion id; a criterion it leaves out is
    /// not rated. The caller checks that every id is one of the stage's criteria.
    /// </summary>
    public (ApplicationScorecard Scorecard, ApplicationEvent Entry) SubmitScorecard(
        WorkflowStage stage,
        Guid reviewerUserId,
        IReadOnlyDictionary<string, CriterionRating?> ratings,
        Recommendation recommendation,
        string? note,
        int workflowVersion,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(ratings);
        var criteria = stage.Scorecard?.Criteria ?? throw new InvalidOperationException($"Stage {stage.Id} has no scorecard.");

        var scorecardId = Guid.CreateVersion7();
        var entry = NewEvent(
            ApplicationEventType.ScorecardSubmitted,
            new ApplicationEventData(ScorecardId: scorecardId, StageId: stage.Id, Recommendation: recommendation.ToString()),
            now,
            reviewerUserId);
        var scorecard = new ApplicationScorecard
        {
            Id = scorecardId,
            OrganizationId = OrganizationId,
            ApplicationId = Id,
            JobId = JobId,
            StageId = stage.Id,
            ReviewerUserId = reviewerUserId,
            Ratings = [.. criteria.Select(c => new CriterionScore(c.Id, c.Label, ratings.GetValueOrDefault(c.Id)))],
            Recommendation = recommendation,
            Note = ApplicationScorecard.Normalize(note),
            WorkflowVersion = workflowVersion,
            TimelineEventId = entry.Id,
            SubmittedAt = now,
        };
        return (scorecard, entry);
    }

    /// <summary>
    /// The timeline event for an email sent to the candidate. <paramref name="eventId"/> is the send's own id, so a
    /// redelivered send finds its event and records nothing twice.
    /// </summary>
    public ApplicationEvent EmailSent(
        Guid eventId, string templateId, string subject, DateTimeOffset now, Guid? actorUserId, Guid? automationRunId = null) =>
        new()
        {
            Id = eventId,
            OrganizationId = OrganizationId,
            ApplicationId = Id,
            Type = ApplicationEventType.EmailSent,
            Data = new ApplicationEventData(AutomationRunId: automationRunId, EmailTemplateId: templateId, EmailSubject: subject),
            ActorUserId = actorUserId,
            OccurredAt = now,
        };

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

/// <summary>Where an application came from. Automation conditions read it as <c>application.source</c>.</summary>
public enum ApplicationSource
{
    /// <summary>A recruiter added it for a candidate who applied another way.</summary>
    Recruiter,

    /// <summary>The candidate applied on the organization's careers page.</summary>
    CareersSite,
}

/// <summary>The outcome of the careers form's bot challenge (Cloudflare Turnstile, when it is configured).</summary>
public enum BotCheckResult
{
    /// <summary>No challenge ran: it is not configured, or the application did not come from the careers page.</summary>
    NotChecked,
    Passed,

    /// <summary>The challenge failed or its token was missing, which also happens with JavaScript turned off.</summary>
    Failed,
}

/// <summary>How an application arrived: its source, answers and the careers form's checks.</summary>
public sealed record ApplicationIntake(
    ApplicationSource Source,
    IReadOnlyList<ApplicationAnswer> Answers,
    string? PrivacyNoticeVersion = null,
    bool HoneypotFilled = false,
    BotCheckResult BotCheck = BotCheckResult.NotChecked)
{
    /// <summary>A recruiter added the application by hand: no answers and no careers-form checks.</summary>
    public static ApplicationIntake Recruiter { get; } = new(ApplicationSource.Recruiter, []);
}
