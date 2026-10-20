using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Auth;

/// <summary>
/// Makes the signed-in user's organization the request's tenant (<see cref="Memberships.ResolveTenantAsync"/>).
/// </summary>
internal sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, TenantContext tenant, ScreenHireDbContext db)
    {
        await Memberships.ResolveTenantAsync(context.User, tenant, db, context.RequestAborted);
        await next(context);
    }
}
