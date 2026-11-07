using FluentValidation;
using ScreenHire.Application.Portal;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// What the careers portal page needs before it asks for an address. <c>enabled</c> is false where the deployment sends no
/// email or has no careers host to link to: the page then says the portal is not available here.
/// <c>turnstileSiteKey</c> is null unless the bot challenge is configured.
/// </summary>
public sealed record PublicPortalForm(string OrganizationName, bool Enabled, string? TurnstileSiteKey);

/// <summary>
/// The address a candidate types to be sent the link to their page. The answer to it is the same whether or not the
/// address has applied, so it tells nobody who has.
/// </summary>
public sealed record PublicPortalLinkRequest(string Email, string? BotCheckToken = null);

/// <summary>What something is waiting on the candidate for.</summary>
public enum PublicPortalWaitingKind
{
    /// <summary>A follow-up questionnaire to answer.</summary>
    Questionnaire,

    /// <summary>An interview to book.</summary>
    Interview,
}

/// <summary>
/// Something the candidate can act on, with the token of the existing careers page for it
/// (<c>/questionnaires/{token}</c> or <c>/interviews/{token}</c>). The name of a questionnaire is the team's wording for it
/// and is not sent.
/// </summary>
public sealed record PublicPortalWaiting(PublicPortalWaitingKind Kind, string Token);

/// <summary>
/// Where an application stands on the four fixed steps every job shares. <c>current</c> is an index into <c>steps</c>, or
/// null once the application ended, and then <c>ended</c> says how. Nothing about the employer's own stages is in it.
/// </summary>
public sealed record PublicPortalFunnel(IReadOnlyList<string> Steps, int? Current, FunnelEnd Ended);

/// <summary>One of the candidate's applications to this organization.</summary>
public sealed record PublicPortalApplication(
    Guid Id,
    string JobTitle,
    DateTimeOffset AppliedAt,
    PublicPortalFunnel Funnel,
    IReadOnlyList<PublicPortalWaiting> Waiting,
    bool HasMessages,
    bool CanReply);

/// <summary>An application the candidate started and saved to finish later; <c>token</c> opens the job's apply page with it.</summary>
public sealed record PublicPortalDraft(string JobTitle, string JobSlug, string Token, DateTimeOffset ExpiresAt);

/// <summary>Everything a candidate's page shows on its first screen.</summary>
public sealed record PublicPortal(
    string OrganizationName,
    string FirstName,
    IReadOnlyList<PublicPortalApplication> Applications,
    IReadOnlyList<PublicPortalDraft> Drafts);

/// <summary>Who wrote a message on the page: the team (an email ScreenHire sent for them) or the candidate.</summary>
public enum PublicPortalMessageDirection
{
    FromTeam,
    FromYou,
}

/// <summary>One message in an application's thread: the email as the candidate got it, or their reply. Plain text only.</summary>
public sealed record PublicPortalMessage(
    PublicPortalMessageDirection Direction, string Subject, string Body, DateTimeOffset At, int AttachmentCount);

/// <summary>An application's thread, oldest first. <c>canReply</c> is false until the team has written once.</summary>
public sealed record PublicPortalMessages(
    string JobTitle, IReadOnlyList<PublicPortalMessage> Messages, bool CanReply, int BodyMax, int MaxAttachments);

/// <summary>A file sent with a reply, as base64 text.</summary>
public sealed record PublicPortalAttachment(string FileName, string ContentBase64);

/// <summary>
/// A reply written on the page. <c>key</c> is the form's own random id (32 hex digits), so sending the same form twice stores one
/// reply.
/// </summary>
public sealed record PublicPortalReplyRequest(string Body, string Key, IReadOnlyList<PublicPortalAttachment>? Attachments = null);

internal sealed class PublicPortalLinkRequestValidator : AbstractValidator<PublicPortalLinkRequest>
{
    public PublicPortalLinkRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().WithMessage("Enter your email address.").MaximumLength(320)
            .EmailAddress().WithMessage("Enter an email address like name@example.com.");
        RuleFor(r => r.BotCheckToken).MaximumLength(4096);
    }
}

internal sealed class PublicPortalReplyRequestValidator : AbstractValidator<PublicPortalReplyRequest>
{
    /// <summary>The most files one reply may carry.</summary>
    public const int MaxAttachments = 3;

    public PublicPortalReplyRequestValidator(Microsoft.Extensions.Options.IOptions<PortalOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        RuleFor(r => r.Body).NotEmpty().WithMessage("Write your message first.")
            .MaximumLength(options.Value.ReplyBodyMax).WithMessage($"Keep your message under {options.Value.ReplyBodyMax:N0} characters.");
        RuleFor(r => r.Key).NotEmpty().Matches("^[0-9a-f]{32}$").WithMessage("Reload the page and try again.");
        RuleFor(r => r.Attachments).Must(files => files is null || files.Count <= MaxAttachments)
            .WithMessage($"Attach up to {MaxAttachments} files.");
        RuleForEach(r => r.Attachments).ChildRules(file =>
        {
            file.RuleFor(f => f.FileName).NotEmpty().MaximumLength(255);
            file.RuleFor(f => f.ContentBase64).NotEmpty().MaximumLength(13_500_000).WithMessage("A file is too large to send.");
        });
    }
}
