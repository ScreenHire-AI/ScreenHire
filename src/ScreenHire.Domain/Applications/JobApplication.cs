using ScreenHire.Domain.Automations;
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
public sealed class JobApplication : IJobScoped
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

    /// <summary>
    /// The organization's own site the careers page was embedded in when the candidate applied (an origin such as
    /// <c>https://www.example.com</c>, one of <see cref="Organizations.Organization.EmbedOrigins"/> at that moment), or
    /// null for an application on the careers site itself or added by hand. <see cref="Source"/> stays
    /// <see cref="ApplicationSource.CareersSite"/>: the frame shows the same pages, notice and checks.
    /// </summary>
    public string? SourceSite { get; init; }

    /// <summary>The candidate's answers to the job's questions (<see cref="ApplicationForm"/>), empty ones left out.</summary>
    public IReadOnlyList<ApplicationAnswer> Answers { get; init; } = [];

    /// <summary>
    /// The version of the privacy notice the candidate saw before anything was collected (CLAUDE.md non-negotiable 4),
    /// or null for an application a recruiter added.
    /// </summary>
    public string? PrivacyNoticeVersion { get; init; }

    /// <summary>
    /// The careers form's hidden trap field had a value. An automation signal for the verification engine
    /// (<c>AUTO_HONEYPOT</c>), recorded and never used to refuse or reject the application.
    /// </summary>
    public bool HoneypotFilled { get; init; }

    /// <summary>The bot challenge's outcome (<c>AUTO_TURNSTILE_FAIL</c>), a signal like <see cref="HoneypotFilled"/>.</summary>
    public BotCheckResult BotCheck { get; init; }

    /// <summary>
    /// When the hiring team kept the candidate in mind: they left this job without a rejection (docs/architecture/
    /// talent-pool.md). The application keeps its stage; it has ended, as one in a Hired, Rejected or Withdrawn stage has.
    /// Null while it is open, and again once a person moves it back to a stage.
    /// </summary>
    public DateTimeOffset? KeptInMindAt { get; private set; }

    /// <summary>
    /// When the candidate said "Keep my details for future roles" on this application's form, or, for an application
    /// added from the talent pool, on the application it came from (the clock never restarts). Null without permission.
    /// It is this application's own permission: a tick on another job's form does not cover it.
    /// </summary>
    public DateTimeOffset? FutureRolesConsentGivenAt { get; private set; }

    /// <summary>The organization's permission period, in months, when the candidate gave it (the form said so).</summary>
    public int? FutureRolesConsentMonths { get; private set; }

    /// <summary>When the candidate took the permission back, or a person removed them from the pool at their request.</summary>
    public DateTimeOffset? FutureRolesWithdrawnAt { get; private set; }

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
            SourceSite = intake.SourceSite,
            Answers = ApplicationForm.Normalize(intake.Answers),
            PrivacyNoticeVersion = intake.PrivacyNoticeVersion,
            HoneypotFilled = intake.HoneypotFilled,
            BotCheck = intake.BotCheck,
            FutureRolesConsentGivenAt = intake.FutureRoles?.GivenAt,
            FutureRolesConsentMonths = intake.FutureRoles?.Months,
        };

        return (application, application.NewEvent(
            ApplicationEventType.Submitted, new ApplicationEventData(ToStageId: firstStage.Id, ApiKeyId: actorApiKeyId), now, actorUserId));
    }

    /// <summary>
    /// Moves the application to <paramref name="target"/>, a stage of its job's workflow, and returns the timeline event.
    /// Moving to the stage it is already in changes nothing and returns null. <paramref name="actorApiKeyId"/> is the API
    /// key that moved it, if one did.
    /// </summary>
    /// <remarks>
    /// Moving an application the team kept in mind brings it back to the job, so it records the move even to the stage it
    /// was in; the caller takes it out of the talent pool.
    /// </remarks>
    public ApplicationEvent? MoveTo(
        WorkflowStage target, DateTimeOffset now, Guid? actorUserId, string? note, Guid? automationRunId = null, Guid? actorApiKeyId = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Id == CurrentStageId && KeptInMindAt is null)
        {
            return null;
        }

        var from = CurrentStageId;
        KeptInMindAt = null;
        CurrentStageId = target.Id;
        StageEnteredAt = now;
        return NewEvent(
            ApplicationEventType.StageChanged,
            new ApplicationEventData(
                from, target.Id, string.IsNullOrWhiteSpace(note) ? null : note.Trim(), AutomationRunId: automationRunId, ApiKeyId: actorApiKeyId),
            now,
            actorUserId);
    }

    /// <summary>
    /// Whether the application has ended: its stage (<paramref name="stage"/>, the current one from its job's workflow) is
    /// Hired, Rejected or Withdrawn, or the team kept the candidate in mind. Nothing sends, runs or counts time on it.
    /// </summary>
    public bool HasEnded(WorkflowStage? stage) => KeptInMindAt is not null || stage?.Kind.IsTerminal() == true;

    /// <summary>
    /// Keeps the candidate in mind: they leave the job without a rejection, and everything on the application stays with
    /// it. <paramref name="note"/> says why, and lives on the returned timeline entry only. Throws when the application has
    /// ended (<paramref name="stage"/> is its current stage) or has no permission for future roles at
    /// <paramref name="now"/>. Never reads verification output (ADR-0004): only the stage and the candidate's own answer.
    /// </summary>
    public ApplicationEvent KeepInMind(WorkflowStage? stage, int organizationMonths, DateTimeOffset now, Guid? actorUserId, string? note)
    {
        if (HasEnded(stage))
        {
            throw new InvalidOperationException("The application has already ended.");
        }

        if (!AllowsFutureRoles(organizationMonths, now))
        {
            throw new InvalidOperationException("The candidate did not give permission to keep their details for future roles.");
        }

        KeptInMindAt = now;
        return NewEvent(
            ApplicationEventType.KeptInMind,
            new ApplicationEventData(Note: string.IsNullOrWhiteSpace(note) ? null : note.Trim()),
            now,
            actorUserId);
    }

    /// <summary>
    /// When this application's permission to keep the candidate's details runs out: the moment it was given plus the
    /// shorter of the period then and <paramref name="organizationMonths"/>, the organization's period now, so lengthening
    /// the setting never extends a permission already given. Null without a permission, or after it was withdrawn.
    /// </summary>
    public DateTimeOffset? FutureRolesUntil(int organizationMonths) =>
        FutureRolesConsentGivenAt is { } given && FutureRolesConsentMonths is { } months && FutureRolesWithdrawnAt is null
            ? given.AddMonths(Math.Min(months, organizationMonths))
            : null;

    /// <summary>Whether the candidate's permission for future roles still holds at <paramref name="now"/>.</summary>
    public bool AllowsFutureRoles(int organizationMonths, DateTimeOffset now) => FutureRolesUntil(organizationMonths) > now;

    /// <summary>The candidate took back their permission for future roles. Withdrawing twice keeps the first time.</summary>
    public void WithdrawFutureRoles(DateTimeOffset now)
    {
        if (FutureRolesConsentGivenAt is not null && FutureRolesWithdrawnAt is null)
        {
            FutureRolesWithdrawnAt = now;
        }
    }

    /// <summary>The permission an application added from the talent pool carries over from this one, if any.</summary>
    public FutureRolesConsent? CarriedFutureRoles() =>
        FutureRolesConsentGivenAt is { } given && FutureRolesConsentMonths is { } months && FutureRolesWithdrawnAt is null
            ? new FutureRolesConsent(given, months)
            : null;

    /// <summary>
    /// The timeline entry for the candidate leaving the talent pool, kept from this application: why, and the other job or
    /// application it names (where they were added, or applied).
    /// </summary>
    public ApplicationEvent LeftTalentPool(
        TalentPoolExit reason, DateTimeOffset now, Guid? actorUserId, Guid? otherJobId = null, Guid? otherApplicationId = null) =>
        NewEvent(
            ApplicationEventType.LeftTalentPool,
            new ApplicationEventData(PoolExit: reason.ToString(), OtherJobId: otherJobId, OtherApplicationId: otherApplicationId),
            now,
            actorUserId);

    /// <summary>The timeline entry for an invitation to apply for <paramref name="jobId"/>, which the email's own entry follows.</summary>
    public ApplicationEvent InvitedToApply(Guid jobId, DateTimeOffset now, Guid? actorUserId) =>
        NewEvent(ApplicationEventType.InvitedToApply, new ApplicationEventData(OtherJobId: jobId), now, actorUserId);

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
    /// A note from the hiring team (<paramref name="text"/>, trimmed) and the timeline entry that records it with its
    /// author and time. The entry names the note and never holds its text, so deleting the note erases it
    /// (<see cref="ApplicationNote"/>). It changes nothing else about the application, and no automation reads it.
    /// </summary>
    public (ApplicationNote Note, ApplicationEvent Entry) AddNote(string text, DateTimeOffset now, Guid? actorUserId, Guid? actorApiKeyId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var note = ApplicationNote.Create(this, text.Trim(), now, actorUserId, actorApiKeyId);
        return (note, NewEvent(ApplicationEventType.NoteAdded, new ApplicationEventData(ApiKeyId: actorApiKeyId, NoteId: note.Id), now, actorUserId));
    }

    /// <summary>
    /// Changes <paramref name="note"/>'s text and returns the timeline entry with the mentions the new text adds. Null
    /// when the text is unchanged, which records nothing. The caller checks <see cref="NotePolicy.MayEdit"/>.
    /// </summary>
    public (ApplicationEvent Entry, IReadOnlyList<ApplicationNoteMention> Added)? EditNote(
        ApplicationNote note, string text, DateTimeOffset now, Guid? actorUserId, Guid? actorApiKeyId = null)
    {
        ArgumentNullException.ThrowIfNull(note);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        EnsureOwn(note);
        if (note.Edit(text.Trim(), now) is not { } added)
        {
            return null;
        }

        return (NewEvent(ApplicationEventType.NoteEdited, new ApplicationEventData(ApiKeyId: actorApiKeyId, NoteId: note.Id), now, actorUserId), added);
    }

    /// <summary>
    /// The timeline entry for deleting <paramref name="note"/>: who and when, never the text. The caller removes the
    /// note's row, which erases its text, and checks <see cref="NotePolicy.MayDelete"/>.
    /// </summary>
    public ApplicationEvent DeleteNote(ApplicationNote note, DateTimeOffset now, Guid? actorUserId, Guid? actorApiKeyId = null)
    {
        ArgumentNullException.ThrowIfNull(note);
        EnsureOwn(note);
        return NewEvent(ApplicationEventType.NoteDeleted, new ApplicationEventData(ApiKeyId: actorApiKeyId, NoteId: note.Id), now, actorUserId);
    }

    private void EnsureOwn(ApplicationNote note)
    {
        if (note.ApplicationId != Id || note.OrganizationId != OrganizationId)
        {
            throw new InvalidOperationException($"Note {note.Id} is not on application {Id}.");
        }
    }

    /// <summary>
    /// A reviewer's scorecard for <paramref name="stage"/>, the job's own copy of the stage's scorecard, and the timeline
    /// entry that records it, which raises the ReviewerDecision trigger. <paramref name="answers"/> holds answers by
    /// question id, checked by the caller with <see cref="ScorecardAnswers.Check"/> against the stage's scorecard; a
    /// question it leaves out is unanswered.
    /// </summary>
    public (ApplicationScorecard Scorecard, ApplicationEvent Entry) SubmitScorecard(
        WorkflowStage stage,
        Guid reviewerUserId,
        IReadOnlyDictionary<string, ScorecardAnswerValue> answers,
        Recommendation recommendation,
        string? note,
        int workflowVersion,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(stage);
        var card = stage.Scorecard ?? throw new InvalidOperationException($"Stage {stage.Id} has no scorecard.");
        return SubmitScorecard(stage.Id, card, stage.ScorecardTemplateId, reviewerUserId, answers, recommendation, note, workflowVersion, now);
    }

    /// <summary>
    /// A reviewer's answers to <paramref name="card"/> given while the application was in <paramref name="stageId"/>,
    /// and the timeline entry that records it. <paramref name="scorecardTemplateId"/> is the library scorecard it came
    /// from (null for a stage copy that names none), which a requested scorecard's task matches; the scorecard keeps its
    /// name as it keeps each question.
    /// </summary>
    public (ApplicationScorecard Scorecard, ApplicationEvent Entry) SubmitScorecard(
        string stageId,
        Scorecard card,
        string? scorecardTemplateId,
        Guid reviewerUserId,
        IReadOnlyDictionary<string, ScorecardAnswerValue> answers,
        Recommendation recommendation,
        string? note,
        int workflowVersion,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stageId);
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(answers);
        if (ScorecardAnswers.Check(card, answers) is { Count: > 0 } errors)
        {
            throw new ArgumentException($"The answers do not fit the scorecard: {errors[0].Message}", nameof(answers));
        }

        var scorecardId = Guid.CreateVersion7();
        var entry = NewEvent(
            ApplicationEventType.ScorecardSubmitted,
            new ApplicationEventData(ScorecardId: scorecardId, StageId: stageId, Recommendation: recommendation.ToString()),
            now,
            reviewerUserId);
        var scorecard = new ApplicationScorecard
        {
            Id = scorecardId,
            OrganizationId = OrganizationId,
            ApplicationId = Id,
            JobId = JobId,
            StageId = stageId,
            ScorecardTemplateId = scorecardTemplateId,
            ScorecardName = card.Name,
            ReviewerUserId = reviewerUserId,
            Answers = ScorecardAnswers.Keep(card, answers),
            Recommendation = recommendation,
            Note = ApplicationScorecard.Normalize(note),
            WorkflowVersion = workflowVersion,
            TimelineEventId = entry.Id,
            SubmittedAt = now,
        };
        return (scorecard, entry);
    }

    /// <summary>
    /// The copy of an email to the candidate, rendered for them, before it goes. <paramref name="sendId"/> is the send's
    /// own id, created once, so a redelivered send finds its copy and its timeline entry.
    /// </summary>
    public ApplicationEmail PrepareEmail(
        Guid sendId, string? templateId, string to, string subject, string textBody, string htmlBody, DateTimeOffset now) =>
        new()
        {
            Id = sendId,
            OrganizationId = OrganizationId,
            ApplicationId = Id,
            TemplateId = templateId,
            To = to,
            Subject = subject,
            TextBody = textBody,
            HtmlBody = htmlBody,
            CreatedAt = now,
        };

    /// <summary>
    /// The timeline event for an email that went to the candidate. It takes the copy's id, the send's own id, so a
    /// redelivered send finds its event and records nothing twice, and it points at the copy, which holds the body.
    /// </summary>
    public ApplicationEvent EmailSent(ApplicationEmail email, DateTimeOffset now, Guid? actorUserId, Guid? automationRunId = null) =>
        new()
        {
            Id = email.Id,
            OrganizationId = OrganizationId,
            ApplicationId = Id,
            Type = ApplicationEventType.EmailSent,
            Data = new ApplicationEventData(
                AutomationRunId: automationRunId, EmailTemplateId: email.TemplateId, EmailSubject: email.Subject, EmailId: email.Id),
            ActorUserId = actorUserId,
            OccurredAt = now,
        };

    /// <summary>
    /// A task a person or an API key gives the hiring team about this application (<paramref name="note"/>, trimmed), for
    /// <paramref name="assigneeUserId"/> or, when null, anyone on the team, and the timeline entry that records it with
    /// who created it. The task starts in the application's current stage; it changes nothing about the application.
    /// </summary>
    public (ApplicationTask Task, ApplicationEvent Entry) AddTask(
        string note, Guid? assigneeUserId, DateTimeOffset now, Guid? actorUserId, Guid? actorApiKeyId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(note);
        var task = new ApplicationTask
        {
            OrganizationId = OrganizationId,
            ApplicationId = Id,
            Kind = ApplicationTaskKind.Task,
            Note = note.Trim(),
            AssigneeUserId = assigneeUserId,
            StageId = CurrentStageId,
            CreatedAt = now,
        };
        return (task, NewEvent(
            ApplicationEventType.TaskCreated, new ApplicationEventData(Note: task.Note, ApiKeyId: actorApiKeyId, TaskId: task.Id), now, actorUserId));
    }

    /// <summary>
    /// Asks <paramref name="assigneeUserId"/> for the library scorecard <paramref name="scorecardTemplateId"/>: a
    /// <see cref="ApplicationTaskKind.Scorecard"/> task, done when they submit that scorecard for this application, and
    /// the timeline entry that records who asked. The task says what to fill in, then the asker's note when there is one.
    /// </summary>
    public (ApplicationTask Task, ApplicationEvent Entry) RequestScorecard(
        string scorecardTemplateId, string scorecardName, Guid assigneeUserId, string? note, DateTimeOffset now, Guid? actorUserId, Guid? actorApiKeyId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scorecardTemplateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(scorecardName);
        var task = new ApplicationTask
        {
            OrganizationId = OrganizationId,
            ApplicationId = Id,
            Kind = ApplicationTaskKind.Scorecard,
            Note = ApplicationTask.ScorecardRequestNote(scorecardName, note),
            AssigneeUserId = assigneeUserId,
            StageId = CurrentStageId,
            ScorecardTemplateId = scorecardTemplateId,
            CreatedAt = now,
        };
        return (task, NewEvent(
            ApplicationEventType.TaskCreated, new ApplicationEventData(Note: task.Note, ApiKeyId: actorApiKeyId, TaskId: task.Id), now, actorUserId));
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

/// <summary>
/// The candidate's permission to keep their details for future roles: when they gave it, and the organization's period
/// in months at that moment, which the form showed them.
/// </summary>
public sealed record FutureRolesConsent(DateTimeOffset GivenAt, int Months);

/// <summary>Why a candidate left the talent pool, as their kept application's Activity says it.</summary>
public enum TalentPoolExit
{
    /// <summary>Their permission ran out.</summary>
    Expired,

    /// <summary>A person on the team took them out.</summary>
    Removed,

    /// <summary>The candidate asked: from the link in an invitation, or a person removed them at their request.</summary>
    CandidateAsked,

    /// <summary>A person added them to another job from the pool.</summary>
    AddedToJob,

    /// <summary>They applied for a job again.</summary>
    AppliedAgain,

    /// <summary>A person moved this application back to a stage of its job.</summary>
    BackInJob,

    /// <summary>A person kept them in mind from another application, which now stands for them in the pool.</summary>
    KeptFromAnotherJob,
}

/// <summary>How an application arrived: its source, answers and the careers form's checks.</summary>
/// <remarks>
/// <c>FutureRoles</c>: the candidate's permission to keep their details for future roles, if they gave it.
/// <c>SourceSite</c>: the organization's own site the careers page was embedded in, if it was (<see cref="JobApplication.SourceSite"/>).
/// </remarks>
public sealed record ApplicationIntake(
    ApplicationSource Source,
    IReadOnlyList<ApplicationAnswer> Answers,
    string? PrivacyNoticeVersion = null,
    bool HoneypotFilled = false,
    BotCheckResult BotCheck = BotCheckResult.NotChecked,
    FutureRolesConsent? FutureRoles = null,
    string? SourceSite = null)
{
    /// <summary>A recruiter added the application by hand: no answers and no careers-form checks.</summary>
    public static ApplicationIntake Recruiter { get; } = new(ApplicationSource.Recruiter, []);
}
