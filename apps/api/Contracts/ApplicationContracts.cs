using FluentValidation;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;

namespace ScreenHire.Api.Contracts;

/// <summary>A candidate: one person per email address within the organization.</summary>
/// <remarks>
/// <c>fullName</c> is the first name, then the last name when there is one. A candidate from before the name was split
/// has their whole name as <c>firstName</c> and no <c>lastName</c>.
/// </remarks>
public sealed record CandidateSummary(
    Guid Id, string FullName, string Email, string? Phone, DateTimeOffset CreatedAt, string FirstName, string? LastName);

/// <summary>A candidate and every application they made to the organization.</summary>
/// <remarks><c>fullName</c>, <c>firstName</c>, <c>lastName</c>: as on <see cref="CandidateSummary"/>.</remarks>
public sealed record CandidateDetail(
    Guid Id,
    string FullName,
    string Email,
    string? Phone,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ApplicationSummary> Applications,
    string FirstName,
    string? LastName);

/// <summary>A stage of the job's workflow, as an application sees it.</summary>
public sealed record ApplicationStage(string Id, string Name, StageKind Kind);

/// <summary>
/// An application in a list. No accommodation flag here: lists reach every reviewer, so it shows only on the
/// application itself.
/// </summary>
/// <remarks>
/// <c>source</c> says whether the candidate applied on the careers page or a recruiter added them. <c>tags</c> are the
/// application's tags, which lists filter by. <c>integrity</c> holds the integrity chips; null until the application
/// has been assessed, and always null for an API key, because integrity evidence is for a person reviewing the
/// application (ADR-0011). <c>slaDueAt</c> is when the application passes its stage's time limit: overdue from then
/// on, which measures the hiring team's delay and never counts against the candidate. Null when the stage has no limit.
/// </remarks>
public sealed record ApplicationSummary(
    Guid Id,
    Guid JobId,
    Guid CandidateId,
    string CandidateName,
    ApplicationStage Stage,
    DateTimeOffset SubmittedAt,
    DateTimeOffset StageEnteredAt,
    string JobTitle,
    ApplicationSource Source,
    IReadOnlyList<string> Tags,
    IntegritySummary? Integrity = null,
    DateTimeOffset? SlaDueAt = null);

/// <summary>
/// One application: the candidate, the job, where it is in the job's workflow, its tags, the candidate's
/// declarations, answers and files.
/// </summary>
/// <remarks>
/// <c>source</c> says whether the candidate applied on the careers page or a recruiter added them. <c>answers</c> are
/// keyed by question id as automation conditions read them (<c>answers.{questionId}</c>); a file answer's value is the
/// upload id of one of <c>files</c>. <c>followUps</c> are the follow-up questionnaires automations sent, oldest first,
/// with their answers once the candidate gave them. <c>slaDueAt</c> is when the application passes its stage's time
/// limit, as on <c>ApplicationSummary</c>.
/// </remarks>
public sealed record ApplicationDetail(
    Guid Id,
    Guid JobId,
    string JobTitle,
    CandidateSummary Candidate,
    ApplicationStage Stage,
    int WorkflowVersion,
    bool AccommodationRequested,
    AiUseDeclaration AiUseDeclaration,
    string? AiUseDetails,
    DateTimeOffset SubmittedAt,
    DateTimeOffset StageEnteredAt,
    IReadOnlyList<string> Tags,
    ApplicationSource Source,
    IReadOnlyList<ApplicationAnswer> Answers,
    IReadOnlyList<ApplicationFileSummary> Files,
    IReadOnlyList<ApplicationFollowUp> FollowUps,
    DateTimeOffset? SlaDueAt = null);

/// <summary>
/// A follow-up questionnaire sent to the candidate. Until it is answered, <c>questions</c> and <c>answers</c> are empty;
/// once it is, <c>questions</c> are the questions as the candidate saw them, so the answers read the same after the
/// workflow changes.
/// </summary>
public sealed record ApplicationFollowUp(
    Guid Id,
    string QuestionnaireId,
    string Name,
    DateTimeOffset FirstSentAt,
    DateTimeOffset LastSentAt,
    DateTimeOffset ExpiresAt,
    int TimesSent,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<Question> Questions,
    IReadOnlyList<ApplicationAnswer> Answers);

/// <summary>A file the candidate uploaded. <c>scanStatus</c> is the malware scan's result; <c>NotScanned</c> means no scanner is configured.</summary>
public sealed record ApplicationFileSummary(
    Guid Id, string QuestionId, Guid UploadId, string FileName, string ContentType, long SizeBytes, MalwareScanStatus ScanStatus, DateTimeOffset UploadedAt);

/// <summary>One entry of an application's timeline, oldest first. Members that do not apply to the type are null.</summary>
/// <remarks>
/// <c>automationRunId</c>: The automation run that made the change; null when a person or the candidate did.
/// <c>actorApiKeyId</c>: The organization API key that made the change (ADR-0011); null otherwise.
/// <c>scorecardId</c>, <c>stageId</c>, <c>recommendation</c>: For <c>ScorecardSubmitted</c>, the scorecard, the stage it
/// rates and the reviewer's overall recommendation (a <c>Recommendation</c> name, as automations read it).
/// <c>emailTemplateId</c>, <c>emailSubject</c>, <c>emailId</c>: For <c>EmailSent</c>, the template it started from (null for a
/// blank email), the subject the candidate received, and the email as it went, for <c>GET /v1/emails/{emailId}</c>; null
/// for an email sent before each email was kept.
/// <c>questionnaireId</c>, <c>questionnaireRequestId</c>: For <c>QuestionnaireSent</c> and <c>QuestionnaireCompleted</c>,
/// the follow-up questionnaire and the request in the application's <c>followUps</c>.
/// <c>note</c>: For <c>NoteAdded</c>, the note's latest text, or null once it was deleted; for <c>StageChanged</c>, the
/// move's note. A mention in a note is <c>@[userId]</c>, a member's user id; the dashboard shows their current name.
/// <c>noteId</c>: For <c>NoteAdded</c>, <c>NoteEdited</c> and <c>NoteDeleted</c>, the note, as the note routes name it.
/// <c>noteEditedAt</c>: For <c>NoteAdded</c>, when the note's text last changed; null if it never did.
/// <c>actorApiKeyName</c>: The name of the API key in <c>actorApiKeyId</c>, so a key's notes show who wrote them.
/// </remarks>
public sealed record ApplicationTimelineEntry(
    Guid Id,
    ApplicationEventType Type,
    DateTimeOffset OccurredAt,
    Guid? ActorUserId,
    string? FromStageId,
    string? ToStageId,
    string? Note,
    string? Tag,
    Guid? AutomationRunId,
    Guid? ActorApiKeyId,
    Guid? ScorecardId,
    string? StageId,
    string? Recommendation,
    string? EmailTemplateId = null,
    string? EmailSubject = null,
    string? QuestionnaireId = null,
    Guid? QuestionnaireRequestId = null,
    Guid? NoteId = null,
    DateTimeOffset? NoteEditedAt = null,
    string? ActorApiKeyName = null,
    Guid? EmailId = null);

/// <summary>A note as it stands: its latest text, its author and when it was written and last edited.</summary>
/// <remarks>
/// <c>text</c>: A mention is <c>@[userId]</c>. <c>authorUserId</c>, <c>authorApiKeyId</c>: The person or the API key
/// that wrote it. <c>editedAt</c>: Null for a note never edited.
/// </remarks>
public sealed record ApplicationNoteDetail(
    Guid Id, Guid ApplicationId, string Text, Guid? AuthorUserId, Guid? AuthorApiKeyId, DateTimeOffset CreatedAt, DateTimeOffset? EditedAt);

/// <summary>
/// A recruiter adds an application by hand, for a candidate who applied another way. The candidate is matched by email
/// within the organization and created if new. The public apply flow is the careers site's.
/// </summary>
public sealed record CreateApplicationRequest(
    CandidateInput Candidate,
    bool AccommodationRequested = false,
    AiUseDeclaration AiUseDeclaration = AiUseDeclaration.NotProvided,
    string? AiUseDetails = null);

/// <summary>The candidate of an application a person or an API key adds.</summary>
/// <remarks>
/// <c>firstName</c> and the optional <c>lastName</c> are the candidate's name. <c>fullName</c> is the older single field:
/// it is kept whole as the first name, with no last name, the way candidates from before the name was split are kept,
/// because nobody's name is split by guesswork. Send <c>firstName</c> or <c>fullName</c>, not both.
/// </remarks>
public sealed record CandidateInput(
    string Email, string? Phone = null, string? FirstName = null, string? LastName = null, string? FullName = null)
{
    /// <summary>The name as it is stored: <c>fullName</c> becomes the first name.</summary>
    internal (string First, string? Last) Name => FirstName is { } first ? (first, LastName) : (FullName ?? "", null);
}

/// <summary>
/// Moves an application to another stage of its job's workflow. Only people move applications here. The optional
/// <c>note</c> is kept on the move's timeline entry.
/// </summary>
public sealed record MoveApplicationRequest(string ToStageId, string? Note = null);

/// <summary>
/// A note from the hiring team on an application, kept on its timeline with the author and the time. Mention a member
/// as <c>@[userId]</c>: they get a notification and an email that never quotes the note.
/// </summary>
public sealed record AddApplicationNoteRequest(string Note);

/// <summary>
/// The note's new text. Only its author edits a note; a mention the new text adds notifies only the person added.
/// </summary>
public sealed record EditApplicationNoteRequest(string Note);

/// <summary>A tag to add to an application. Tags compare ignoring case; conditions read them as <c>application.tags</c>.</summary>
public sealed record AddApplicationTagRequest(string Tag);

internal static class ApplicationMappings
{
    public static CandidateSummary ToSummary(this Candidate candidate) =>
        new(candidate.Id, candidate.FullName, candidate.Email, candidate.Phone, candidate.CreatedAt, candidate.FirstName, candidate.LastName);

    /// <summary>The application's stage in <paramref name="job"/>'s workflow, falling back to the bare id.</summary>
    public static ApplicationStage StageIn(this JobApplication application, Job job) =>
        job.Workflow.FindStage(application.CurrentStageId) is { } stage
            ? new(stage.Id, stage.Name, stage.Kind)
            : new(application.CurrentStageId, application.CurrentStageId, StageKind.Custom);

    /// <summary>
    /// When the application passes the time limit of its stage in <paramref name="job"/>'s current workflow, or null.
    /// The screens compare it with their own clock, so an application turns overdue on screen without an event.
    /// </summary>
    public static DateTimeOffset? SlaDueAt(this JobApplication application, Job job) =>
        job.Workflow.FindStage(application.CurrentStageId)?.SlaDueAt(application.StageEnteredAt);

    public static ApplicationSummary ToSummary(this JobApplication application, Job job, Candidate candidate, IntegritySummary? integrity = null) =>
        new(application.Id, application.JobId, application.CandidateId, candidate.FullName, application.StageIn(job),
            application.SubmittedAt, application.StageEnteredAt, job.Title, application.Source, application.Tags, integrity,
            application.SlaDueAt(job));

    public static ApplicationDetail ToDetail(
        this JobApplication application, Job job, Candidate candidate, IReadOnlyList<ApplicationFile> files,
        IReadOnlyList<QuestionnaireRequest> followUps) =>
        new(application.Id, application.JobId, job.Title, candidate.ToSummary(), application.StageIn(job),
            application.WorkflowVersion, application.AccommodationRequested, application.AiUseDeclaration,
            application.AiUseDetails, application.SubmittedAt, application.StageEnteredAt, application.Tags,
            application.Source,
            application.Answers, [.. files.Select(f => f.ToSummary())], [.. followUps.Select(f => f.ToFollowUp())], application.SlaDueAt(job));

    public static ApplicationFollowUp ToFollowUp(this QuestionnaireRequest request) =>
        new(request.Id, request.QuestionnaireId, request.QuestionnaireName, request.FirstSentAt, request.LastSentAt, request.ExpiresAt,
            request.TimesSent, request.CompletedAt, request.Questions, request.Answers);

    public static ApplicationFileSummary ToSummary(this ApplicationFile file) =>
        new(file.Id, file.QuestionId, file.UploadId, file.FileName, file.ContentType, file.SizeBytes, file.ScanStatus, file.UploadedAt);

    /// <summary>
    /// <paramref name="note"/> is the note a <c>NoteAdded</c> entry names, or null once it was deleted, and
    /// <paramref name="apiKeyName"/> the name of the key that made the change.
    /// </summary>
    public static ApplicationTimelineEntry ToTimelineEntry(this ApplicationEvent entry, ApplicationNote? note = null, string? apiKeyName = null)
    {
        var added = entry.Type == ApplicationEventType.NoteAdded;
        return new(entry.Id, entry.Type, entry.OccurredAt, entry.ActorUserId, entry.Data.FromStageId, entry.Data.ToStageId,
            added ? note?.Text : entry.Data.Note, entry.Data.Tag, entry.Data.AutomationRunId, entry.Data.ApiKeyId, entry.Data.ScorecardId,
            entry.Data.StageId, entry.Data.Recommendation, entry.Data.EmailTemplateId, entry.Data.EmailSubject, entry.Data.QuestionnaireId,
            entry.Data.QuestionnaireRequestId, entry.Data.NoteId, added ? note?.EditedAt : null, apiKeyName, entry.Data.EmailId);
    }

    public static ApplicationNoteDetail ToDetail(this ApplicationNote note) =>
        new(note.Id, note.ApplicationId, note.Text, note.AuthorUserId, note.AuthorApiKeyId, note.CreatedAt, note.EditedAt);
}

internal sealed class CreateApplicationRequestValidator : AbstractValidator<CreateApplicationRequest>
{
    public CreateApplicationRequestValidator()
    {
        RuleFor(r => r.Candidate).NotNull();
        RuleFor(r => r.Candidate).Must(c => (c.FirstName is null) != (c.FullName is null))
            .OverridePropertyName("Candidate.FirstName")
            .WithMessage("Give the candidate's firstName, or the older fullName, not both.")
            .When(r => r.Candidate is not null);
        RuleFor(r => r.Candidate.FirstName).NotEmpty().MaximumLength(Candidate.MaxNamePartLength)
            .When(r => r.Candidate is { FirstName: not null });
        RuleFor(r => r.Candidate.LastName).MaximumLength(Candidate.MaxNamePartLength).When(r => r.Candidate is not null);
        RuleFor(r => r.Candidate.LastName).Null().WithMessage("A lastName goes with firstName: fullName is kept whole.")
            .When(r => r.Candidate is { FullName: not null });
        RuleFor(r => r.Candidate.FullName).NotEmpty().MaximumLength(Candidate.MaxFirstNameLength)
            .When(r => r.Candidate is { FullName: not null });
        RuleFor(r => r.Candidate.Email).NotEmpty().MaximumLength(320).EmailAddress().When(r => r.Candidate is not null);
        RuleFor(r => r.Candidate.Phone).MaximumLength(50).When(r => r.Candidate is not null);
        RuleFor(r => r.AiUseDeclaration).IsInEnum();
        RuleFor(r => r.AiUseDetails).MaximumLength(2000);
    }
}

internal sealed class MoveApplicationRequestValidator : AbstractValidator<MoveApplicationRequest>
{
    public MoveApplicationRequestValidator()
    {
        RuleFor(r => r.ToStageId).NotEmpty().MaximumLength(40);
        RuleFor(r => r.Note).MaximumLength(1000);
    }
}

internal sealed class AddApplicationNoteRequestValidator : AbstractValidator<AddApplicationNoteRequest>
{
    public AddApplicationNoteRequestValidator()
    {
        RuleFor(r => r.Note).NotEmpty().Must(note => !string.IsNullOrWhiteSpace(note)).WithMessage("Write the note first.")
            .MaximumLength(JobApplication.MaxNoteLength);
    }
}

internal sealed class EditApplicationNoteRequestValidator : AbstractValidator<EditApplicationNoteRequest>
{
    public EditApplicationNoteRequestValidator()
    {
        RuleFor(r => r.Note).NotEmpty().Must(note => !string.IsNullOrWhiteSpace(note)).WithMessage("Write the note first.")
            .MaximumLength(JobApplication.MaxNoteLength);
    }
}

internal sealed class AddApplicationTagRequestValidator : AbstractValidator<AddApplicationTagRequest>
{
    public AddApplicationTagRequestValidator()
    {
        RuleFor(r => r.Tag).NotEmpty().Must(tag => !string.IsNullOrWhiteSpace(tag)).WithMessage("Name the tag first.")
            .Must(tag => tag is null || tag.Trim().Length <= JobApplication.MaxTagLength)
            .WithMessage($"A tag is at most {JobApplication.MaxTagLength} characters.");
    }
}
