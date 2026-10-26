using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Candidates;

/// <summary>
/// A person who applied to one of the organization's jobs. One candidate per email address within an organization, so
/// a second application reuses the record. Candidate-supplied data only (CLAUDE.md non-negotiable 5).
/// </summary>
/// <remarks>
/// The name is two parts, as the apply form asks for it: <see cref="FirstName"/> and an optional <see cref="LastName"/>.
/// Candidates from before the split keep their whole name as the first name, with no last name: nobody's name is split
/// by guesswork. <see cref="FullName"/> is derived from the two and stored, so everything that reads it keeps working.
/// </remarks>
public sealed class Candidate : ITenantOwned
{
    /// <summary>The most a first or a last name may hold from a form or the API.</summary>
    public const int MaxNamePartLength = 100;

    /// <summary>
    /// The most <see cref="FirstName"/> holds: a name from before the split, kept whole, or the API's single
    /// <c>fullName</c>.
    /// </summary>
    public const int MaxFirstNameLength = 200;

    /// <summary>The most <see cref="FullName"/> holds: a first name, a space and a last name.</summary>
    public const int MaxFullNameLength = MaxFirstNameLength + 1 + MaxNamePartLength;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public string FirstName { get; private set; } = "";

    /// <summary>Null when the candidate gave none, and for every candidate from before the name was split.</summary>
    public string? LastName { get; private set; }

    /// <summary>The first name, then the last name when there is one. Derived; set only through <see cref="Create"/>.</summary>
    public string FullName { get; private set; } = "";

    public required string Email { get; init; }

    /// <summary>The lookup key: <see cref="Email"/> trimmed and lower-cased.</summary>
    public string NormalizedEmail { get; private init; } = "";

    public string? Phone { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public static Candidate Create(Guid organizationId, string firstName, string? lastName, string email, string? phone, DateTimeOffset now)
    {
        var first = firstName.Trim();
        var last = string.IsNullOrWhiteSpace(lastName) ? null : lastName.Trim();
        return new()
        {
            OrganizationId = organizationId,
            FirstName = first,
            LastName = last,
            FullName = JoinName(first, last),
            Email = email.Trim(),
            NormalizedEmail = Normalize(email),
            Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
            CreatedAt = now,
        };
    }

    /// <summary>"Casey Morgan", or "Casey" with no last name.</summary>
    public static string JoinName(string firstName, string? lastName) =>
        string.IsNullOrWhiteSpace(lastName) ? firstName : $"{firstName} {lastName}";

    public static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
