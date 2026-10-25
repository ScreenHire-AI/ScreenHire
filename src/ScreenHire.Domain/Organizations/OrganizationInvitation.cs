namespace ScreenHire.Domain.Organizations;

/// <summary>
/// An emailed invitation to join an organization with a role. Single use and expiring: accepting it sets
/// <see cref="AcceptedAt"/> once, and an Owner or Admin can revoke it before then. Only the SHA-256 hash of the token
/// is stored; the token itself exists only in the email.
/// </summary>
public sealed class OrganizationInvitation : ITenantOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    /// <summary>The invited address, trimmed and lower-cased. Accepting signs in as the user with this address.</summary>
    public required string Email { get; init; }

    public required OrganizationRole Role { get; init; }

    /// <summary>
    /// The custom role it offers, when <see cref="Role"/> is Custom. No foreign key (ADR-0014): the id is only resolved
    /// under row-level security, and an old closed invitation must not keep the role from being deleted.
    /// </summary>
    public Guid? CustomRoleId { get; init; }

    /// <summary>The job access the person joins with (ADR-0015). All unless the inviter chose Only jobs they're on.</summary>
    public JobAccess JobAccess { get; init; } = JobAccess.All;

    /// <summary>Hex SHA-256 of the token in the emailed link.</summary>
    public required string TokenHash { get; init; }

    public Guid InvitedByUserId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    public Guid? AcceptedByUserId { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>Neither accepted, revoked nor expired at <paramref name="now"/>.</summary>
    public bool IsOpen(DateTimeOffset now) => AcceptedAt is null && RevokedAt is null && ExpiresAt > now;

    /// <summary>Revokes an open invitation; false when it was already accepted, revoked or expired.</summary>
    public bool Revoke(DateTimeOffset now)
    {
        if (!IsOpen(now))
        {
            return false;
        }

        RevokedAt = now;
        return true;
    }
}
