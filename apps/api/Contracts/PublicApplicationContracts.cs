using FluentValidation;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Api.Contracts;

/// <summary>An organization as its careers pages show it, with its branding (each part null for ScreenHire's).</summary>
public sealed record PublicOrganization(string Name, string Slug, PublicBranding Branding);

/// <summary>
/// One question of a job's application form, as a candidate sees it. Knockout settings stay on the server: the form
/// accepts every answer, and a person reviews what follows from it.
/// </summary>
/// <remarks>
/// <c>maxLength</c> is the effective limit for text and URL questions. <c>acceptedFileTypes</c> lists the extensions a
/// file question takes (lower case, with the dot), already narrowed to the types ScreenHire accepts.
/// </remarks>
public sealed record PublicQuestion(
    string Id,
    QuestionType Type,
    string Label,
    bool Required,
    string? HelpText,
    IReadOnlyList<QuestionOption>? Options,
    decimal? Min,
    decimal? Max,
    int? MaxLength,
    IReadOnlyList<string>? AcceptedFileTypes);

/// <summary>Limits on candidate uploads. <c>origin</c> is where upload links point, for the page's content security policy.</summary>
public sealed record PublicUploadSettings(long MaxBytes, string Origin);

/// <summary>
/// Everything the careers apply page needs for one open job. The resume question comes first
/// (<see cref="ApplicationForm"/>). <c>turnstileSiteKey</c> is null unless the bot challenge is configured.
/// <c>telemetry</c> is a new telemetry session for the form; the page keeps the first one it gets.
/// <c>talentPoolMonths</c> is how long the organization keeps a candidate's details for future roles if they say so,
/// which the form's "Keep my details for future roles" box tells them.
/// </summary>
public sealed record PublicApplicationForm(
    PublicOrganization Organization,
    PublicJob Job,
    IReadOnlyList<PublicQuestion> Questions,
    AiUsePolicy AiUsePolicy,
    PublicUploadSettings Uploads,
    string? TurnstileSiteKey,
    PublicTelemetrySettings Telemetry,
    int TalentPoolMonths);

/// <summary>Asks for a link to upload one file for a file question. <c>sizeBytes</c> is the size the candidate's device reports.</summary>
public sealed record UploadRequest(string FileName, long SizeBytes);

/// <summary>
/// Where and how to upload one file, and until when: send its bytes with <c>method</c> to <c>uploadUrl</c> with
/// <c>headers</c>, then refer to it by <c>uploadId</c>.
/// </summary>
public sealed record UploadTicket(
    Guid UploadId, Uri UploadUrl, string Method, IReadOnlyDictionary<string, string> Headers, DateTimeOffset ExpiresAt, long MaxBytes);

/// <summary>An upload that was found and checked. Its <c>uploadId</c> is the answer to its file question.</summary>
public sealed record UploadedFile(Guid UploadId, string FileName, string ContentType, long SizeBytes);

/// <summary>
/// A candidate's application from the careers page. The candidate is matched by email within the organization and
/// created if new; an existing candidate's details are never changed by a public submission.
/// </summary>
/// <remarks>
/// <c>accommodationRequested</c> is the "I use assistive technology" switch: it turns timing and typing signals off and is
/// never held against anyone. <c>privacyNoticeVersion</c> is the notice the candidate was shown before anything was
/// collected. <c>honeypotFilled</c> and <c>botCheckToken</c> come from the form's bot checks; they are recorded as signals
/// and never refuse a submission.
/// The rest is session context for the integrity engine, all optional and none of it ever a reason to refuse:
/// <c>userAgent</c> is the candidate's browser as the careers server received it; <c>formStartedAt</c> is when the careers
/// server first showed the form; <c>automationMarkers</c> are markers the page detected, such as <c>webdriver</c>;
/// <c>deviceFingerprint</c> is the page's device identifier, which the API hashes per organization and never stores as
/// sent, and drops where the job's device-check basis is <c>Off</c>, or <c>Consent</c> without
/// <c>deviceCheckConsent</c>. The candidate's address comes from the connection (<c>X-Forwarded-For</c> from a trusted
/// proxy). <c>keepForFutureRoles</c> is the candidate's own, optional "Keep my details for future roles": the
/// organization may keep this application for the period the form showed and invite them to apply for other jobs.
/// <c>telemetrySessionToken</c> binds the form's telemetry to the application, and <c>telemetry</c> is the last
/// page's snapshot, which travels with the form post rather than a beacon so it is stored before analysis starts; an
/// invalid one is dropped, never a reason to refuse.
/// </remarks>
public sealed record PublicApplicationRequest(
    PublicCandidateInput Candidate,
    string PrivacyNoticeVersion,
    IReadOnlyList<ApplicationAnswer>? Answers = null,
    bool AccommodationRequested = false,
    AiUseDeclaration AiUseDeclaration = AiUseDeclaration.NotProvided,
    string? AiUseDetails = null,
    bool HoneypotFilled = false,
    string? BotCheckToken = null,
    string? UserAgent = null,
    DateTimeOffset? FormStartedAt = null,
    IReadOnlyList<string>? AutomationMarkers = null,
    string? DeviceFingerprint = null,
    bool DeviceCheckConsent = false,
    string? TelemetrySessionToken = null,
    TelemetrySnapshotInput? Telemetry = null,
    bool KeepForFutureRoles = false);

/// <summary>The candidate as the apply form asks: a first name, an optional last name, the email and an optional phone.</summary>
public sealed record PublicCandidateInput(string FirstName, string Email, string? LastName = null, string? Phone = null);

/// <summary>Confirms that an application arrived.</summary>
public sealed record PublicApplicationReceipt(Guid ApplicationId, DateTimeOffset SubmittedAt);

/// <summary>
/// A follow-up questionnaire as its careers page shows it, through the signed link in the candidate's email.
/// <c>questions</c> are listed only while the link is <c>Open</c>; knockout settings stay on the server.
/// </summary>
public sealed record PublicQuestionnaire(
    PublicOrganization Organization, string JobTitle, string Name, PublicQuestionnaireStatus Status, IReadOnlyList<PublicQuestion> Questions);

/// <summary>
/// Whether a follow-up questionnaire's link can still be answered: <c>Answered</c> once the candidate sent their answers,
/// <c>Expired</c> after the link's time ran out, <c>Closed</c> when the questionnaire, the job or the application is no
/// longer open.
/// </summary>
public enum PublicQuestionnaireStatus
{
    Open,
    Answered,
    Expired,
    Closed,
}

/// <summary>The candidate's answers to a follow-up questionnaire, keyed by question id as on the apply form.</summary>
public sealed record PublicQuestionnaireAnswers(IReadOnlyList<ApplicationAnswer> Answers);

internal static class PublicApplicationMappings
{
    public static PublicQuestion ToPublic(this Question question, Func<IReadOnlyList<string>?, IReadOnlyList<string>> acceptedFileTypes) =>
        new(question.Id, question.Type, question.Label, question.Required, question.HelpText, question.Options,
            question.Type == QuestionType.Number ? question.Min : null,
            question.Type == QuestionType.Number ? question.Max : null,
            question.Type is QuestionType.ShortText or QuestionType.LongText or QuestionType.Url ? ApplicationForm.MaxLengthOf(question) : null,
            question.Type == QuestionType.File ? acceptedFileTypes(question.AcceptedFileTypes) : null);

    /// <summary>Whether a job with <paramref name="policy"/> needs the candidate to say whether they used AI.</summary>
    public static bool RequiresDeclaration(this AiUsePolicy policy) => policy != AiUsePolicy.Allowed;
}

internal sealed class PublicApplicationRequestValidator : AbstractValidator<PublicApplicationRequest>
{
    public PublicApplicationRequestValidator()
    {
        RuleFor(r => r.Candidate).NotNull();
        RuleFor(r => r.Candidate.FirstName).NotEmpty().WithMessage("Enter your first name.")
            .MaximumLength(Candidate.MaxNamePartLength).WithMessage($"Keep your first name to {Candidate.MaxNamePartLength} characters or fewer.")
            .When(r => r.Candidate is not null);
        RuleFor(r => r.Candidate.LastName)
            .MaximumLength(Candidate.MaxNamePartLength).WithMessage($"Keep your last name to {Candidate.MaxNamePartLength} characters or fewer.")
            .When(r => r.Candidate is not null);
        RuleFor(r => r.Candidate.Email).NotEmpty().WithMessage("Enter your email address.").MaximumLength(320)
            .EmailAddress().WithMessage("Enter an email address like name@example.com.")
            .When(r => r.Candidate is not null);
        RuleFor(r => r.Candidate.Phone).MaximumLength(50).When(r => r.Candidate is not null);
        RuleFor(r => r.PrivacyNoticeVersion).NotEmpty().MaximumLength(40);
        RuleFor(r => r.Answers).Must(a => a is null || a.Count <= 100).WithMessage("At most 100 answers.");
        RuleForEach(r => r.Answers).ChildRules(answer =>
        {
            answer.RuleFor(a => a.QuestionId).NotEmpty().MaximumLength(40);
            answer.RuleFor(a => a.Value).MaximumLength(ApplicationForm.LongTextMaxLength);
            answer.RuleFor(a => a.Values).Must(v => v is null || v.Count <= 50).WithMessage("At most 50 choices.");
        });
        RuleFor(r => r.AiUseDeclaration).IsInEnum();
        RuleFor(r => r.AiUseDetails).MaximumLength(2000);
        RuleFor(r => r.BotCheckToken).MaximumLength(4096);
        RuleFor(r => r.UserAgent).MaximumLength(1024);
        RuleFor(r => r.AutomationMarkers).Must(m => m is null || m.Count <= 20).WithMessage("At most 20 automation markers.");
        RuleForEach(r => r.AutomationMarkers).NotEmpty().MaximumLength(40);
        RuleFor(r => r.DeviceFingerprint).MaximumLength(256);
        RuleFor(r => r.TelemetrySessionToken).MaximumLength(1024);
    }
}

internal sealed class UploadRequestValidator : AbstractValidator<UploadRequest>
{
    public UploadRequestValidator()
    {
        RuleFor(r => r.FileName).NotEmpty().MaximumLength(255);
        RuleFor(r => r.SizeBytes).GreaterThan(0).WithMessage("This file is empty.");
    }
}
