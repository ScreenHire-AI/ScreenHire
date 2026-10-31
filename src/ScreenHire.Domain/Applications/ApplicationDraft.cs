using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Applications;

/// <summary>
/// An application a candidate saved on the careers apply form to finish later (apply-flow.md §7): what they typed, kept
/// under their email, with a signed link emailed to that address. It is not an application: no event, automation or
/// verification check ever reads it, so nothing in it can end anything before the candidate sends it.
/// </summary>
/// <remarks>
/// Answers only: the contact details, the answers to the non-file questions, the assistive-technology switch (so a
/// resumed form never starts telemetry on someone who switched it on) and the AI-use answer. Never files, telemetry, the
/// device check, a fingerprint or time on the form. A draft is found by its link, not by its email, so nobody who knows
/// an address can replace the answers saved under it. Sending the application deletes the job's drafts for that email;
/// the daily sweep deletes the rest at <see cref="ExpiresAt"/>.
/// </remarks>
public sealed class ApplicationDraft : IJobScoped
{
    /// <summary>How long a draft is kept, from its first save.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    /// <summary>The most drafts one address may be sent links for, in one organization, in <see cref="LinkWindow"/>.</summary>
    public const int LinksPerAddress = 5;

    public static readonly TimeSpan LinkWindow = TimeSpan.FromDays(1);

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid JobId { get; init; }

    /// <summary>Trimmed and lower-cased, as <see cref="Candidate.Normalize"/> matches candidates.</summary>
    public required string Email { get; init; }

    public string FirstName { get; private set; } = "";

    public string? LastName { get; private set; }

    public string? Phone { get; private set; }

    /// <summary>The answers to the form's non-file questions, empty ones left out.</summary>
    public IReadOnlyList<ApplicationAnswer> Answers { get; private set; } = [];

    public bool AccommodationRequested { get; private set; }

    public AiUseDeclaration AiUseDeclaration { get; private set; }

    public string? AiUseDetails { get; private set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary><see cref="Lifetime"/> after the first save; later saves do not move it.</summary>
    public DateTimeOffset ExpiresAt { get; init; }

    public static ApplicationDraft Create(Guid organizationId, Guid jobId, string email, ApplicationDraftContent content, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(email);
        var draft = new ApplicationDraft
        {
            OrganizationId = organizationId,
            JobId = jobId,
            Email = Candidate.Normalize(email),
            CreatedAt = now,
            ExpiresAt = now + Lifetime,
        };
        draft.Update(content, now);
        return draft;
    }

    /// <summary>Replaces what the draft holds. The email stays: a different address is a different draft.</summary>
    public void Update(ApplicationDraftContent content, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(content);
        FirstName = content.FirstName.Trim();
        LastName = string.IsNullOrWhiteSpace(content.LastName) ? null : content.LastName.Trim();
        Phone = string.IsNullOrWhiteSpace(content.Phone) ? null : content.Phone.Trim();
        Answers = ApplicationForm.Normalize(content.Answers);
        AccommodationRequested = content.AccommodationRequested;
        AiUseDeclaration = content.AiUseDeclaration;
        AiUseDetails = string.IsNullOrWhiteSpace(content.AiUseDetails) ? null : content.AiUseDetails.Trim();
        UpdatedAt = now;
    }

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    /// <summary>
    /// The question ids whose answer in <paramref name="answers"/> is the one this draft saved: work the candidate did
    /// in an earlier sitting, which the submit-velocity check does not time again.
    /// </summary>
    public IReadOnlyList<string> CarriedOver(IReadOnlyList<ApplicationAnswer> answers)
    {
        ArgumentNullException.ThrowIfNull(answers);
        var saved = Answers.ToDictionary(a => a.QuestionId, StringComparer.Ordinal);
        return
        [
            .. ApplicationForm.Normalize(answers)
                .Where(a => saved.TryGetValue(a.QuestionId, out var before) && SameAnswer(before, a))
                .Select(a => a.QuestionId),
        ];
    }

    private static bool SameAnswer(ApplicationAnswer left, ApplicationAnswer right) =>
        string.Equals(left.Value, right.Value, StringComparison.Ordinal)
        && (left.Values ?? []).SequenceEqual(right.Values ?? [], StringComparer.Ordinal);
}

/// <summary>What a candidate saved: their details, their answers to the non-file questions, the switch and the AI-use answer.</summary>
public sealed record ApplicationDraftContent(
    string FirstName,
    string? LastName,
    string? Phone,
    IReadOnlyList<ApplicationAnswer> Answers,
    bool AccommodationRequested,
    AiUseDeclaration AiUseDeclaration,
    string? AiUseDetails);
