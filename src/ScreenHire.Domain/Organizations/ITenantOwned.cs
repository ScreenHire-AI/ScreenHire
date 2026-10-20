namespace ScreenHire.Domain.Organizations;

/// <summary>
/// A row that belongs to exactly one organization. The persistence layer gives every implementing entity the tenant
/// query filter, and its table a row-level security policy (ADR-0008, CLAUDE.md non-negotiable 6).
/// </summary>
public interface ITenantOwned
{
    Guid OrganizationId { get; }
}
