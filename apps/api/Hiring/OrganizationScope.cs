using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Hiring;

/// <summary>
/// Runs work in a scope of its own that acts for the whole organization, with no signed-in user, as the careers pages and
/// the worker do. Interview availability and booking need it (docs/architecture/interview-scheduling.md): a member on Only
/// jobs they're on would not see an interviewer's bookings on other jobs, and a booking check that misses them could
/// double-book someone. The route checks the member may act on the job before it calls this, and returns only what the
/// work found about the job it was asked about: free or busy, never what the busy time is.
/// </summary>
/// <remarks>
/// The ASP.NET Core scope is the request's and carries the member's tenant; this one is separate, so its DbContext, outbox
/// and services all share one connection and transaction that sees every job.
/// </remarks>
internal static class OrganizationScope
{
    public static async Task<T> RunAsync<T>(IServiceScopeFactory scopes, Guid organizationId, Func<IServiceProvider, Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(work);
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetOrganization(organizationId);
        return await work(scope.ServiceProvider);
    }
}
