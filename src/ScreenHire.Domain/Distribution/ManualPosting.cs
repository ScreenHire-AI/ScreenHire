using System.Text;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Distribution;

/// <summary>
/// A job the team posted themselves on a board ScreenHire does not integrate with (LinkedIn before partner status, Indeed's
/// free listings, a local or niche board), kept in the same Job postings list as the automatic boards
/// (docs/architecture/job-distribution.md, "Posted by you").
/// </summary>
/// <remarks>
/// <para>
/// The board's apply link carries <c>src=</c><see cref="SourceKey"/>, so applications through it name the board in Source and
/// in Reports. The key is fixed when the posting is saved: the link may already be pasted on the board, so renaming the
/// board never changes it.
/// </para>
/// <para>
/// <see cref="ListingUrl"/> is only a link for the person. Nothing fetches or reads it (CLAUDE.md non-negotiable 5).
/// </para>
/// <para>
/// <see cref="PostedContent"/> is the job as it was when the person saved the posting or last marked it updated: what they
/// copied. The row says the job changed since while any copied field differs from the live job (<see cref="ChangedSince"/>).
/// </para>
/// </remarks>
public sealed class ManualPosting : IJobScoped
{
    /// <summary>The longest board name.</summary>
    public const int BoardNameMaxLength = 80;

    /// <summary>The longest source key: what an application's <c>SourceBoard</c> holds.</summary>
    public const int SourceKeyMaxLength = 40;

    /// <summary>The longest listing address.</summary>
    public const int ListingUrlMaxLength = 2048;

    private ManualPosting()
    {
    }

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid JobId { get; init; }

    /// <summary>The board as the person named it, such as <c>LinkedIn</c> or <c>Riverside Jobs Weekly</c>.</summary>
    public string BoardName { get; private set; } = "";

    /// <summary>The <c>src</c> value of the board's apply link: lower-case letters, digits and hyphens, unique on the job.</summary>
    public string SourceKey { get; init; } = "";

    /// <summary>Where the listing is, when the person gave it. An absolute http or https address, never fetched.</summary>
    public string? ListingUrl { get; private set; }

    /// <summary>The day the person posted the job on the board.</summary>
    public DateOnly PostedOn { get; private set; }

    /// <summary>The day the listing ends on the board, when it has one.</summary>
    public DateOnly? EndsOn { get; private set; }

    /// <summary>The job's content as the person last copied it: when they saved the posting, or marked it updated.</summary>
    public JobSnapshot PostedContent { get; private set; } = null!;

    /// <summary>When <see cref="PostedContent"/> was taken.</summary>
    public DateTimeOffset ContentAt { get; private set; }

    /// <summary>When the person said the listing is down. Null while it is up.</summary>
    public DateTimeOffset? TakenDownAt { get; private set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsTakenDown => TakenDownAt is not null;

    public static ManualPosting Create(
        Guid organizationId,
        Guid jobId,
        string boardName,
        string sourceKey,
        string? listingUrl,
        DateOnly postedOn,
        DateOnly? endsOn,
        JobSnapshot content,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(boardName);
        ArgumentNullException.ThrowIfNull(content);
        if (!IsSourceKey(sourceKey))
        {
            throw new ArgumentException("A source key is lower-case letters, digits and hyphens.", nameof(sourceKey));
        }

        return new ManualPosting
        {
            OrganizationId = organizationId,
            JobId = jobId,
            BoardName = boardName.Trim(),
            SourceKey = sourceKey,
            ListingUrl = Blank(listingUrl),
            PostedOn = postedOn,
            EndsOn = endsOn,
            PostedContent = content,
            ContentAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Changes what the person said about the listing. The source key stays, so links already shared keep counting.</summary>
    public void Edit(string boardName, string? listingUrl, DateOnly postedOn, DateOnly? endsOn, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(boardName);
        BoardName = boardName.Trim();
        ListingUrl = Blank(listingUrl);
        PostedOn = postedOn;
        EndsOn = endsOn;
        UpdatedAt = now;
    }

    /// <summary>
    /// The person posted the job on this board again after taking it down: the row takes the new details and
    /// <paramref name="content"/>, the job as it is now. The source key stays, so the old link counts as well.
    /// </summary>
    public void PostAgain(string boardName, string? listingUrl, DateOnly postedOn, DateOnly? endsOn, JobSnapshot content, DateTimeOffset now)
    {
        Edit(boardName, listingUrl, postedOn, endsOn, now);
        MarkUpdated(content, now);
        TakenDownAt = null;
    }

    /// <summary>The person updated the listing to <paramref name="content"/>, the job as it is now.</summary>
    public void MarkUpdated(JobSnapshot content, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(content);
        PostedContent = content;
        ContentAt = now;
        UpdatedAt = now;
    }

    /// <summary>The person took the listing down. Returns false when it was already down.</summary>
    public bool TakeDown(DateTimeOffset now)
    {
        if (TakenDownAt is not null)
        {
            return false;
        }

        TakenDownAt = now;
        UpdatedAt = now;
        return true;
    }

    /// <summary>The copied fields of <paramref name="job"/> that differ from what was posted, in the panel's order.</summary>
    public IReadOnlyList<PostedField> ChangedSince(JobSnapshot job) => ChangedFields(PostedContent, job);

    /// <summary>
    /// The fields a person copies to a board that differ between <paramref name="posted"/> and <paramref name="job"/>. Only
    /// those count: a changed summary, department or end date is not on the listing. The slug is, inside the apply link.
    /// </summary>
    public static IReadOnlyList<PostedField> ChangedFields(JobSnapshot posted, JobSnapshot job)
    {
        ArgumentNullException.ThrowIfNull(posted);
        ArgumentNullException.ThrowIfNull(job);
        var changed = new List<PostedField>();
        if (posted.Title != job.Title)
        {
            changed.Add(PostedField.Title);
        }

        if (posted.WorkplaceType != job.WorkplaceType
            || !posted.Locations.SequenceEqual(job.Locations)
            || !posted.ApplicantLocationRequirements.SequenceEqual(job.ApplicantLocationRequirements))
        {
            changed.Add(PostedField.Location);
        }

        if (posted.EmploymentType != job.EmploymentType)
        {
            changed.Add(PostedField.Type);
        }

        if (posted.Salary != job.Salary)
        {
            changed.Add(PostedField.Salary);
        }

        if (posted.Description != job.Description)
        {
            changed.Add(PostedField.Description);
        }

        if (posted.Slug != job.Slug)
        {
            changed.Add(PostedField.Link);
        }

        return changed;
    }

    /// <summary>Whether <paramref name="value"/> can be a source key: what the careers app passes on and the column holds.</summary>
    public static bool IsSourceKey(string? value) =>
        !string.IsNullOrEmpty(value)
        && value.Length <= SourceKeyMaxLength
        && value.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');

    /// <summary>
    /// The source key a board name makes: lower case, every run of other characters one hyphen, none at either end, cut to
    /// the column. <c>LinkedIn</c> is <c>linkedin</c>, <c>Riverside Jobs Weekly</c> is <c>riverside-jobs-weekly</c>. Empty
    /// when the name has no letter or digit. The dashboard's <c>sourceKeyFor</c> follows the same rule.
    /// </summary>
    public static string SourceKeyFor(string boardName)
    {
        ArgumentNullException.ThrowIfNull(boardName);
        var key = new StringBuilder();
        var hyphen = false;
        foreach (var c in boardName.Normalize(NormalizationForm.FormD))
        {
            var lower = char.ToLowerInvariant(c);
            if (lower is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                if (hyphen && key.Length > 0)
                {
                    key.Append('-');
                }

                key.Append(lower);
                hyphen = false;
            }
            else if (char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                hyphen = true;
            }
        }

        var text = key.ToString();
        return text.Length <= SourceKeyMaxLength ? text : text[..SourceKeyMaxLength].TrimEnd('-');
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>A field of the job a person copies to a board by hand. <c>Link</c> is the apply link, which holds the job's slug.</summary>
public enum PostedField
{
    Title,
    Location,
    Type,
    Salary,
    Description,
    Link,
}
