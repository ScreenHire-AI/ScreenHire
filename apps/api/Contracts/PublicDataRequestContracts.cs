using FluentValidation;
using ScreenHire.Domain.Privacy;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// What the careers data request page needs before it asks for an address. <c>enabled</c> is false where the deployment
/// sends no email or has no careers host to link to: the page then keeps its "write to the employer" text.
/// <c>turnstileSiteKey</c> is null unless the bot challenge is configured.
/// </summary>
public sealed record PublicDataRequestForm(string OrganizationName, bool Enabled, string? TurnstileSiteKey);

/// <summary>
/// The address a candidate types to be sent the link that opens their choices. The answer to it is the same whether or not
/// the address has applied, so it tells nobody who has.
/// </summary>
public sealed record PublicDataRequestLinkRequest(string Email, string? BotCheckToken = null);

/// <summary>What a valid emailed link opens: the organization the candidate will be asking, by name.</summary>
public sealed record PublicDataRequestLinkStatus(string OrganizationName);

/// <summary>The candidate's choice: a copy of their data, or its erasure.</summary>
public sealed record PublicDataRequestChoice(DataRequestKind Kind);

/// <summary>
/// The request the choice created, or the one that was already open. It never carries the team's due date, which is a
/// target on their dashboard and not a promise to the candidate.
/// </summary>
public sealed record PublicDataRequestReceipt(Guid Id, DataRequestKind Kind, DateTimeOffset ReceivedAt);

/// <summary>A short-lived address that downloads the candidate's copy.</summary>
public sealed record PublicDataDownload(string Url);

internal sealed class PublicDataRequestLinkRequestValidator : AbstractValidator<PublicDataRequestLinkRequest>
{
    public PublicDataRequestLinkRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().WithMessage("Enter your email address.").MaximumLength(320)
            .EmailAddress().WithMessage("Enter an email address like name@example.com.");
        RuleFor(r => r.BotCheckToken).MaximumLength(4096);
    }
}

internal sealed class PublicDataRequestChoiceValidator : AbstractValidator<PublicDataRequestChoice>
{
    public PublicDataRequestChoiceValidator() => RuleFor(r => r.Kind).IsInEnum().WithMessage("Choose a copy of your data or erasure.");
}
