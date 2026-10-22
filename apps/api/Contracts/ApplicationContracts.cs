using FluentValidation;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;

namespace ScreenHire.Api.Contracts;

/// <summary>A candidate: one person per email address within the organization.</summary>
public sealed record CandidateSummary(Guid Id, string FullName, string Email, string? Phone, DateTimeOffset CreatedAt);

/// <summary>A candidate and every application they made to the organization.</summary>
public sealed record CandidateDetail(
    Guid Id, string FullName, string Email, string? Phone, DateTimeOffset CreatedAt, IReadOnlyList<ApplicationSummary> Applications);

/// <summary>A stage of the job's workflow, as an application sees it.</summary>
public sealed record ApplicationStage(string Id, string Name, StageKind Kind);

/// <summary>
/// An application in a list. No accommodation flag here: lists reach every reviewer, so it shows only on the
/// application itself.
/// </summary>
/// <remarks>
/// <c>source</c> says whether the candidate applied on the careers page or a recruiter added them. <c>integrity</c> holds
/// the integrity chips; null until the application has been assessed, and always null for an API key, because integrity
/// evidence is for a person reviewing the application (ADR-0011).
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
    IntegritySummary? Integrity = null);

/// <summary>
/// One application: the candidate, the job, where it is in the job's workflow, its tags, the candidate's
/// declarations, answers and files.
/// </summary>
/// <remarks>
/// <c>source</c> says whether the candidate applied on the careers page or a recruiter added them. <c>answers</c> are
/// keyed by question id as automation conditions read them (<c>answers.{questionId}</c>); a file answer's value is the
/// upload id of one of <c>files</c>.
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
    IReadOnlyList<ApplicationFileSummary> Files);

/// <summary>A file the candidate uploaded. <c>scanStatus</c> is the malware scan's result; <c>NotScanned</c> means no scanner is configured.</summary>
public sealed record ApplicationFileSummary(
    Guid Id, string QuestionId, Guid UploadId, string FileName, string ContentType, long SizeBytes, MalwareScanStatus ScanStatus, DateTimeOffset UploadedAt);

/// <summary>One entry of an application's timeline, oldest first. Members that do not apply to the type are null.</summary>
/// <remarks>
/// <c>automationRunId</c>: The automation run that made the change; null when a person or the candidate did.
/// <c>actorApiKeyId</c>: The organization API key that made the change (ADR-0011); null otherwise.
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
    Guid? ActorApiKeyId = null);

/// <summary>
/// A recruiter adds an application by hand, for a candidate who applied another way. The candidate is matched by email
/// within the organization and created if new. The public apply flow is the careers site's.
/// </summary>
public sealed record CreateApplicationRequest(
    CandidateInput Candidate,
    bool AccommodationRequested = false,
    AiUseDeclaration AiUseDeclaration = AiUseDeclaration.NotProvided,
    string? AiUseDetails = null);

public sealed record CandidateInput(string FullName, string Email, string? Phone = null);

/// <summary>Moves an application to another stage of its job's workflow. Only people move applications here.</summary>
public sealed record MoveApplicationRequest(string ToStageId, string? Note = null);

internal static class ApplicationMappings
{
    public static CandidateSummary ToSummary(this Candidate candidate) =>
        new(candidate.Id, candidate.FullName, candidate.Email, candidate.Phone, candidate.CreatedAt);

    /// <summary>The application's stage in <paramref name="job"/>'s workflow, falling back to the bare id.</summary>
    public static ApplicationStage StageIn(this JobApplication application, Job job) =>
        job.Workflow.FindStage(application.CurrentStageId) is { } stage
            ? new(stage.Id, stage.Name, stage.Kind)
            : new(application.CurrentStageId, application.CurrentStageId, StageKind.Custom);

    public static ApplicationSummary ToSummary(this JobApplication application, Job job, Candidate candidate, IntegritySummary? integrity = null) =>
        new(application.Id, application.JobId, application.CandidateId, candidate.FullName, application.StageIn(job),
            application.SubmittedAt, application.StageEnteredAt, job.Title, application.Source, integrity);

    public static ApplicationDetail ToDetail(
        this JobApplication application, Job job, Candidate candidate, IReadOnlyList<ApplicationFile> files) =>
        new(application.Id, application.JobId, job.Title, candidate.ToSummary(), application.StageIn(job),
            application.WorkflowVersion, application.AccommodationRequested, application.AiUseDeclaration,
            application.AiUseDetails, application.SubmittedAt, application.StageEnteredAt, application.Tags,
            application.Source,
            application.Answers, [.. files.Select(f => f.ToSummary())]);

    public static ApplicationFileSummary ToSummary(this ApplicationFile file) =>
        new(file.Id, file.QuestionId, file.UploadId, file.FileName, file.ContentType, file.SizeBytes, file.ScanStatus, file.UploadedAt);

    public static ApplicationTimelineEntry ToTimelineEntry(this ApplicationEvent entry) =>
        new(entry.Id, entry.Type, entry.OccurredAt, entry.ActorUserId, entry.Data.FromStageId, entry.Data.ToStageId, entry.Data.Note,
            entry.Data.Tag, entry.Data.AutomationRunId, entry.Data.ApiKeyId);
}

internal sealed class CreateApplicationRequestValidator : AbstractValidator<CreateApplicationRequest>
{
    public CreateApplicationRequestValidator()
    {
        RuleFor(r => r.Candidate).NotNull();
        RuleFor(r => r.Candidate.FullName).NotEmpty().MaximumLength(200).When(r => r.Candidate is not null);
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
