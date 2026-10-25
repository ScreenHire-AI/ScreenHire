namespace ScreenHire.Infrastructure.Tenancy;

/// <summary>
/// Who the current unit of work acts for. The EF tenant and job-scope query filters and the Postgres row-level security
/// session settings (<c>app.tenant_id</c>, <c>app.user_id</c>, <c>app.job_scope</c>, <c>app.member_id</c>) all read it,
/// so a context with no organization sees no tenant rows at all.
/// </summary>
public interface ITenantContext
{
    /// <summary>The organization whose rows are visible, or null for none.</summary>
    Guid? OrganizationId { get; }

    /// <summary>The signed-in user, or null. Lets a user read their own memberships before an organization is chosen.</summary>
    Guid? UserId { get; }

    /// <summary>The organization API key the request authenticated with, or null. Change history records it as the actor.</summary>
    Guid? ApiKeyId { get; }

    /// <summary>The signed-in user's membership of <see cref="OrganizationId"/>, once their job access is resolved.</summary>
    Guid? MemberId { get; }

    /// <summary>
    /// Whether every job of the organization is visible (ADR-0015). True with no user: an API key, a worker handler, the
    /// careers pages and the seeders act for the whole organization. With a user it is false until their job access is
    /// resolved, so a path that forgets to resolve it sees no jobs rather than all of them.
    /// </summary>
    bool SeesAllJobs { get; }
}

/// <summary>
/// The scoped, settable <see cref="ITenantContext"/>. The API sets it from the signed-in user's membership, the
/// public careers endpoints from the organization slug in the route, and the worker from a tenant message's
/// organization (<c>TenantMessageMiddleware</c>). A system message that walks every organization sets each in turn.
/// Nothing else may change it.
/// </summary>
public sealed class TenantContext : ITenantContext
{
    public Guid? OrganizationId { get; private set; }

    public Guid? UserId { get; private set; }

    public Guid? ApiKeyId { get; private set; }

    public Guid? MemberId { get; private set; }

    public bool SeesAllJobs => UserId is null || seesAllJobs;

    private bool seesAllJobs;

    // A new user or organization drops the job access resolved for the old one, so it can only be too narrow, never too wide.
    public void SetUser(Guid userId)
    {
        UserId = userId;
        ClearMemberScope();
    }

    public void SetOrganization(Guid organizationId)
    {
        OrganizationId = organizationId;
        ClearMemberScope();
    }

    /// <summary>
    /// The signed-in user's membership of the organization and whether they see every job: Owner, Admin, or job access
    /// All (<c>JobAccessRules.SeesAllJobs</c>). Set after <see cref="SetUser"/> and <see cref="SetOrganization"/>.
    /// </summary>
    public void SetMemberScope(Guid memberId, bool seesAll)
    {
        MemberId = memberId;
        seesAllJobs = seesAll;
    }

    private void ClearMemberScope()
    {
        MemberId = null;
        seesAllJobs = false;
    }

    public void SetApiKey(Guid apiKeyId) => ApiKeyId = apiKeyId;
}
