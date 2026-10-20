using FluentValidation;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Api.Contracts;

/// <summary>An organization as its careers pages show it.</summary>
/// <remarks><c>branding</c> is null when the organization has no brand colour: careers pages use ScreenHire's.</remarks>
public sealed record PublicOrganization(string Name, string Slug, PublicBranding? Branding);

/// <summary>
/// The careers-page colours derived from the organization's brand colour, each contrast-checked on the server
/// (docs/design/design-system.md §7): <c>brand</c> fills, <c>onBrand</c> is text on it (4.5:1), <c>brandText</c> is
/// brand-coloured text on the page (4.5:1) and <c>brandUi</c> is borders and focus rings (3:1). All <c>#rrggbb</c>.
/// </summary>
public sealed record PublicBranding(string Brand, string OnBrand, string BrandText, string BrandUi);

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
/// </summary>
public sealed record PublicApplicationForm(
    PublicOrganization Organization,
    PublicJob Job,
    IReadOnlyList<PublicQuestion> Questions,
    AiUsePolicy AiUsePolicy,
    PublicUploadSettings Uploads,
    string? TurnstileSiteKey);

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
/// </remarks>
public sealed record PublicApplicationRequest(
    CandidateInput Candidate,
    string PrivacyNoticeVersion,
    IReadOnlyList<ApplicationAnswer>? Answers = null,
    bool AccommodationRequested = false,
    AiUseDeclaration AiUseDeclaration = AiUseDeclaration.NotProvided,
    string? AiUseDetails = null,
    bool HoneypotFilled = false,
    string? BotCheckToken = null);

/// <summary>Confirms that an application arrived.</summary>
public sealed record PublicApplicationReceipt(Guid ApplicationId, DateTimeOffset SubmittedAt);

internal static class PublicApplicationMappings
{
    public static PublicOrganization ToPublic(this Organization organization) =>
        new(organization.Name, organization.Slug, organization.GetBrandPalette() is { } palette
            ? new PublicBranding(palette.Brand, palette.OnBrand, palette.BrandText, palette.BrandUi)
            : null);

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
        RuleFor(r => r.Candidate.FullName).NotEmpty().WithMessage("Enter your full name.").MaximumLength(200)
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
