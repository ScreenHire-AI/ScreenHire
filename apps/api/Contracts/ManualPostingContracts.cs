using FluentValidation;
using ScreenHire.Domain.Distribution;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// A job the team posted themselves on a board we do not integrate with (docs/architecture/job-distribution.md, "Posted by
/// you"). <c>trackedUrl</c> is the job's careers page with <c>src=</c><c>sourceKey</c>: applications through it name the
/// board in Source and in Reports. <c>listingUrl</c> is where the listing is, as the person gave it; nothing fetches it.
/// <c>changedFields</c> are the fields of the job that differ from what was posted (empty when it is up to date), until
/// the person marks the posting updated. <c>contentAt</c> is when the posted text was taken. <c>takenDownAt</c> is when
/// the person said the listing is down; while it is null and the job is closed or <c>endsOn</c> has passed, the row says
/// it is still posted.
/// </summary>
public sealed record ManualPostingStatus(
    Guid Id,
    string BoardName,
    string SourceKey,
    string TrackedUrl,
    string? ListingUrl,
    DateOnly PostedOn,
    DateOnly? EndsOn,
    DateTimeOffset ContentAt,
    DateTimeOffset? TakenDownAt,
    IReadOnlyList<PostedField> ChangedFields,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// The job's postings by the team, and the address their tracked links extend: the job's careers page. A tracked link is
/// <c>jobUrl</c> with <c>src=</c> and a source key, which the dashboard shows before a posting is saved.
/// </summary>
public sealed record ManualPostingList(string JobUrl, IReadOnlyList<ManualPostingStatus> Postings);

/// <summary>
/// Applications to one job through one link: <c>source</c> is <c>careers</c> (the careers page with no board tag), an
/// automatic board's id, or a posting by the team's source key. A source with no application is left out. An application
/// through the organization's own site, or added by a recruiter, has no row here.
/// </summary>
public sealed record SourceApplications(string Source, int Applications);

/// <summary>
/// Saving a posting the team made. <c>sourceKey</c> is the <c>src</c> of the link the person copied, lower-case letters,
/// digits and hyphens; left out, it is made from <c>boardName</c>. It cannot be an automatic board's id. <c>postedOn</c>
/// left out is today (UTC); the dashboard sends the person's own today. A board the job already has a posting on that was
/// taken down is posted again: the row takes these details and the job as it is now.
/// </summary>
public sealed record CreateManualPostingRequest(
    string BoardName,
    string? SourceKey = null,
    string? ListingUrl = null,
    DateOnly? PostedOn = null,
    DateOnly? EndsOn = null);

/// <summary>Changing what the team said about a posting. The source key stays, so links already shared keep counting.</summary>
public sealed record UpdateManualPostingRequest(string BoardName, string? ListingUrl, DateOnly PostedOn, DateOnly? EndsOn = null);

internal static class ManualPostingRules
{
    /// <summary>An absolute http or https address, or blank. Anything else (a <c>javascript:</c> link) is refused.</summary>
    public static bool IsListingUrl(string? url) =>
        string.IsNullOrWhiteSpace(url)
        || (url.Trim().Length <= ManualPosting.ListingUrlMaxLength
            && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            && uri.Scheme is "https" or "http");

    public static bool IsBoardName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= ManualPosting.BoardNameMaxLength && ManualPosting.SourceKeyFor(name).Length > 0;

    public const string BoardNameMessage = "Name the board in up to 80 characters, with at least one letter or digit.";

    public const string ListingUrlMessage = "Give the listing's full address, starting with https://.";

    public const string EndsOnMessage = "The end date cannot be before the day it was posted.";
}

internal sealed class CreateManualPostingRequestValidator : AbstractValidator<CreateManualPostingRequest>
{
    public CreateManualPostingRequestValidator()
    {
        RuleFor(r => r.BoardName).Must(ManualPostingRules.IsBoardName).WithMessage(ManualPostingRules.BoardNameMessage);
        RuleFor(r => r.SourceKey).Must(key => key is null || ManualPosting.IsSourceKey(key))
            .WithMessage("A source key is up to 40 lower-case letters, digits and hyphens.");
        RuleFor(r => r.ListingUrl).Must(ManualPostingRules.IsListingUrl).WithMessage(ManualPostingRules.ListingUrlMessage);
        RuleFor(r => r.EndsOn).Must((r, endsOn) => endsOn is null || r.PostedOn is null || endsOn >= r.PostedOn)
            .WithMessage(ManualPostingRules.EndsOnMessage);
    }
}

internal sealed class UpdateManualPostingRequestValidator : AbstractValidator<UpdateManualPostingRequest>
{
    public UpdateManualPostingRequestValidator()
    {
        RuleFor(r => r.BoardName).Must(ManualPostingRules.IsBoardName).WithMessage(ManualPostingRules.BoardNameMessage);
        RuleFor(r => r.ListingUrl).Must(ManualPostingRules.IsListingUrl).WithMessage(ManualPostingRules.ListingUrlMessage);
        RuleFor(r => r.EndsOn).Must((r, endsOn) => endsOn is null || endsOn >= r.PostedOn).WithMessage(ManualPostingRules.EndsOnMessage);
    }
}
