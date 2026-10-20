namespace ScreenHire.Domain.Organizations;

/// <summary>
/// A user's membership of an organization. The model allows many organizations per user; the dashboard shows one.
/// </summary>
public sealed class OrganizationMember : ITenantOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    /// <summary>The signed-in user (an identity in the persistence layer; the domain only knows the id).</summary>
    public Guid UserId { get; init; }

    public OrganizationRole Role { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }
}

public enum OrganizationRole
{
    Owner,
    Admin,
    Recruiter,
    Viewer,
}
