using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Auth;

/// <summary>
/// For routes that create organization data: a signed-in user who has not created or joined an organization yet gets
/// 403 with a problem body instead of a database error. Routes that read or change existing rows need no filter; with no
/// organization they find nothing and answer 404 or an empty list.
/// </summary>
internal static class OrganizationRequirement
{
    public static RouteHandlerBuilder RequireOrganization(this RouteHandlerBuilder builder) =>
        builder
            .AddEndpointFilter(async (invocation, next) =>
                invocation.HttpContext.RequestServices.GetRequiredService<ITenantContext>().OrganizationId is null
                    ? TypedResults.Problem(
                        title: "No organization",
                        detail: "Create or join an organization first.",
                        statusCode: StatusCodes.Status403Forbidden)
                    : await next(invocation))
            .ProducesProblem(StatusCodes.Status403Forbidden);
}
