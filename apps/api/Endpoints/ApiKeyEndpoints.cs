using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// The organization's API keys (ADR-0011): create, list and revoke, for Owners and Admins in a signed-in session. An API
/// key cannot call these routes: they keep the cookie-only default policy, so a key gets 401. The key itself is in the
/// create response only and is never logged; the database keeps its hash.
/// </summary>
internal static class ApiKeyEndpoints
{
    public static IEndpointRouteBuilder MapApiKeyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/organization/api-keys").WithTags("API keys").RequireAuthorization();
        group.MapGet("/", ListApiKeys).WithName("ListApiKeys")
            .RequirePermission(Permission.ManageApiKeys)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/", CreateApiKey).WithName("CreateApiKey")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.ManageApiKeys)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/{keyId:guid}", GetApiKey).WithName("GetApiKey")
            .RequirePermission(Permission.ManageApiKeys)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/{keyId:guid}/revoke", RevokeApiKey).WithName("RevokeApiKey")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.ManageApiKeys)
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
    }

    private static async Task<Ok<List<ApiKeySummary>>> ListApiKeys(ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var keys = await db.ApiKeys.AsNoTracking().OrderByDescending(k => k.CreatedAt).ToListAsync(cancellationToken);
        return TypedResults.Ok(keys.Select(k => k.ToSummary()).ToList());
    }

    // A key never carries more than its creator's role (ADR-0014): a Write key needs every hiring write.
    private static async Task<Results<Created<CreatedApiKey>, ValidationProblem, ProblemHttpResult>> CreateApiKey(
        CreateApiKeyRequest request,
        IValidator<CreateApiKeyRequest> validator,
        ScreenHireDbContext db,
        ITenantContext tenant,
        TimeProvider time,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var organizationId = tenant.OrganizationId!.Value;
        if (request.Scopes.Contains(ApiKeyScope.Write)
            && await RoleAccess.ForMemberAsync(db, organizationId, tenant.UserId!.Value, cancellationToken) is var creator
            && (creator is null || !RolePermissions.HiringWrites.IsSubsetOf(creator.Permissions)))
        {
            return TeamProblems.MoreAccess();
        }

        var (token, prefix, hash) = ApiKeyTokens.Create(organizationId);
        var key = new ApiKey
        {
            OrganizationId = organizationId,
            Name = request.Name.Trim(),
            Prefix = prefix,
            SecretHash = hash,
            Scopes = [.. request.Scopes.Order()],
            PermitsPerMinute = request.PermitsPerMinute,
            CreatedByUserId = tenant.UserId,
            CreatedAt = time.GetUtcNow(),
        };
        db.ApiKeys.Add(key);
        await db.SaveChangesAsync(cancellationToken);

        // The one response that carries the key: no cache may keep it.
        context.Response.Headers.CacheControl = "no-store";
        return TypedResults.Created($"/v1/organization/api-keys/{key.Id}", new CreatedApiKey(key.ToSummary(), token));
    }

    private static async Task<Results<Ok<ApiKeySummary>, NotFound>> GetApiKey(
        Guid keyId, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var key = await db.ApiKeys.AsNoTracking().SingleOrDefaultAsync(k => k.Id == keyId, cancellationToken);
        return key is null ? TypedResults.NotFound() : TypedResults.Ok(key.ToSummary());
    }

    // Revoking twice is not an error: the key stays revoked from the first time. The row stays, so the list shows when.
    private static async Task<Results<Ok<ApiKeySummary>, NotFound>> RevokeApiKey(
        Guid keyId, ScreenHireDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var key = await db.ApiKeys.SingleOrDefaultAsync(k => k.Id == keyId, cancellationToken);
        if (key is null)
        {
            return TypedResults.NotFound();
        }

        key.Revoke(time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(key.ToSummary());
    }
}
