using Microsoft.AspNetCore.Identity;

namespace ScreenHire.Infrastructure.Identity;

/// <summary>
/// A person who signs in to the recruiter dashboard. Users are global (one person can belong to several
/// organizations), so this table has no <c>OrganizationId</c> and no row-level security; memberships carry the tenant.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    public ApplicationUser()
    {
        Id = Guid.CreateVersion7();
        SecurityStamp = Guid.NewGuid().ToString("N");
    }

    /// <summary>The name shown in the dashboard, from the identity provider or the email address.</summary>
    public string? DisplayName { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }
}
