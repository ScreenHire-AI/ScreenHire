namespace ScreenHire.Domain.Organizations;

/// <summary>
/// A tenant row that belongs to one job. Besides the tenant filter, the persistence layer gives every implementing
/// entity the job-scope query filter, and its table a restrictive row-level security policy, so a member on Only jobs
/// they're on sees the row only through a job whose hiring team they are on (ADR-0015).
/// </summary>
public interface IJobScoped : ITenantOwned
{
    Guid JobId { get; }
}

/// <summary>A tenant row that belongs to one application, and so to that application's job (ADR-0015). See <see cref="IJobScoped"/>.</summary>
public interface IApplicationScoped : ITenantOwned
{
    Guid ApplicationId { get; }
}
