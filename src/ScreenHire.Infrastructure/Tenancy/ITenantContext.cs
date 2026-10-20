namespace ScreenHire.Infrastructure.Tenancy;

/// <summary>
/// Who the current unit of work acts for. The EF tenant query filter and the Postgres row-level security session
/// settings (<c>app.tenant_id</c>, <c>app.user_id</c>) both read it, so a context with no organization sees no
/// tenant rows at all.
/// </summary>
public interface ITenantContext
{
    /// <summary>The organization whose rows are visible, or null for none.</summary>
    Guid? OrganizationId { get; }

    /// <summary>The signed-in user, or null. Lets a user read their own memberships before an organization is chosen.</summary>
    Guid? UserId { get; }
}

/// <summary>
/// The scoped, settable <see cref="ITenantContext"/>. The API sets it from the signed-in user's membership, and the
/// public careers endpoints from the organization slug in the route. Nothing else may change it.
/// </summary>
public sealed class TenantContext : ITenantContext
{
    public Guid? OrganizationId { get; private set; }

    public Guid? UserId { get; private set; }

    public void SetUser(Guid userId) => UserId = userId;

    public void SetOrganization(Guid organizationId) => OrganizationId = organizationId;
}
