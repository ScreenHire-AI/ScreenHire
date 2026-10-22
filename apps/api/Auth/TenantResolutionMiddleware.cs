using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Auth;

/// <summary>
/// Makes the request's organization the tenant: an API key's own organization, or the signed-in user's
/// (<see cref="Memberships.ResolveTenantAsync"/>). A key sets no user, so it sees its organization's rows and nothing
/// that row-level security shows a user across organizations, such as their other memberships.
/// </summary>
internal sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, TenantContext tenant, ScreenHireDbContext db)
    {
        if (context.User.ApiKeyOrganizationId() is { } organizationId)
        {
            tenant.SetOrganization(organizationId);
        }
        else
        {
            await Memberships.ResolveTenantAsync(context.User, tenant, db, context.RequestAborted);
        }

        await next(context);
    }
}
