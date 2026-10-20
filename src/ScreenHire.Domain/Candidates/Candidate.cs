using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Candidates;

/// <summary>
/// A person who applied to one of the organization's jobs. One candidate per email address within an organization, so
/// a second application reuses the record. Candidate-supplied data only (CLAUDE.md non-negotiable 5).
/// </summary>
public sealed class Candidate : ITenantOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public required string FullName { get; set; }

    public required string Email { get; init; }

    /// <summary>The lookup key: <see cref="Email"/> trimmed and lower-cased.</summary>
    public string NormalizedEmail { get; private init; } = "";

    public string? Phone { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public static Candidate Create(Guid organizationId, string fullName, string email, string? phone, DateTimeOffset now) =>
        new()
        {
            OrganizationId = organizationId,
            FullName = fullName.Trim(),
            Email = email.Trim(),
            NormalizedEmail = Normalize(email),
            Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
            CreatedAt = now,
        };

    public static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
