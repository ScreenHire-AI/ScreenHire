using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Api.Endpoints;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.OAuth;

/// <summary>
/// The routes a signed-in person uses to decide on an assistant's request and to manage what is connected
/// (docs/architecture/oauth-server.md, ADR-0028). They are session-only, like the API keys page: an assistant cannot call
/// them. The same people may connect an assistant as may make an API key, <see cref="Permission.ManageApiKeys"/>, and a
/// connection never holds more than they do. The dashboard's consent page, on the origin where the sign-in cookie is,
/// calls these; the protocol endpoints are in <see cref="OAuthEndpoints"/>.
/// </summary>
internal static class OAuthConsentEndpoints
{
    public static IEndpointRouteBuilder MapOAuthConsentEndpoints(this IEndpointRouteBuilder app)
    {
        var consent = app.MapGroup("/v1/oauth").WithTags("OAuth").RequireAuthorization();
        consent.MapGet("/authorization-request", GetAuthorizationRequest).WithName("GetOAuthAuthorizationRequest")
            .RequirePermission(Permission.ManageApiKeys)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);
        consent.MapPost("/authorize", Authorize).WithName("DecideOAuthAuthorization")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.ManageApiKeys)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        var connections = app.MapGroup("/v1/organization/oauth-connections").WithTags("OAuth").RequireAuthorization();
        connections.MapGet("/", ListOAuthConnections).WithName("ListOAuthConnections")
            .RequirePermission(Permission.ManageApiKeys)
            .Produces(StatusCodes.Status401Unauthorized);
        connections.MapPost("/{connectionId:guid}/revoke", RevokeOAuthConnection).WithName("RevokeOAuthConnection")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.ManageApiKeys)
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
    }

    // Query names are camelCase like every other route; the consent page maps the OAuth parameters (client_id and so on).
    private static async Task<Results<Ok<OAuthAuthorizationRequestInfo>, ProblemHttpResult>> GetAuthorizationRequest(
        string? clientId, string? redirectUri, string? responseType, string? scope, string? resource,
        string? codeChallenge, string? codeChallengeMethod,
        OAuthServer server, ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken)
    {
        var (valid, problem) = await server.CheckAsync(
            new AuthorizationParameters(clientId, redirectUri, responseType, scope, resource, codeChallenge, codeChallengeMethod, null),
            cancellationToken);
        if (valid is null)
        {
            return CannotConnect(problem!);
        }

        var (write, verification) = await server.GrantableAsync(cancellationToken);
        var organizationName = await db.Organizations.AsNoTracking()
            .Where(o => o.Id == tenant.OrganizationId).Select(o => o.Name).SingleAsync(cancellationToken);
        var redirect = new Uri(valid.RedirectUri);
        var offers = valid.Requested.Select(s => s switch
        {
            ApiKeyScope.Write when !write => new OAuthScopeOffer(s, false, "Your role does not allow every change this would let the assistant make."),
            ApiKeyScope.VerificationRead when !verification => new OAuthScopeOffer(s, false, "Only an owner can give access to verification results."),
            _ => new OAuthScopeOffer(s, true, null),
        }).ToList();
        return TypedResults.Ok(new OAuthAuthorizationRequestInfo(
            valid.Client.Name, redirect.Authority, OAuthRedirects.IsLoopback(redirect), organizationName, offers));
    }

    private static async Task<Results<Ok<OAuthAuthorizeResult>, ProblemHttpResult>> Authorize(
        OAuthAuthorizeRequest request, OAuthServer server, HttpContext context, CancellationToken cancellationToken)
    {
        var (valid, problem) = await server.CheckAsync(
            new AuthorizationParameters(
                request.ClientId, request.RedirectUri, request.ResponseType, request.Scope, request.Resource,
                request.CodeChallenge, request.CodeChallengeMethod, request.State),
            cancellationToken);
        if (valid is null)
        {
            return CannotConnect(problem!);
        }

        // The redirect carries a code or the refusal: no cache may keep it.
        context.Response.Headers.CacheControl = "no-store";
        if (!request.Approve)
        {
            return TypedResults.Ok(new OAuthAuthorizeResult(server.Deny(valid)));
        }

        // Only what the assistant asked for, and never more than the person holds (ADR-0011, ADR-0021).
        var chosen = request.GrantedScopes.Where(s => Enum.IsDefined(s) && valid.Requested.Contains(s)).Distinct().ToList();
        var (write, verification) = await server.GrantableAsync(cancellationToken);
        if (chosen.Contains(ApiKeyScope.Write) && !write)
        {
            return TeamProblems.MoreAccess();
        }

        if (chosen.Contains(ApiKeyScope.VerificationRead) && !verification)
        {
            return TeamProblems.OwnersOnly("Only an owner can give an assistant access to verification results.");
        }

        return TypedResults.Ok(new OAuthAuthorizeResult(await server.ApproveAsync(valid, chosen, cancellationToken)));
    }

    private static async Task<Ok<List<OAuthConnectionSummary>>> ListOAuthConnections(ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var rows = await (
            from key in db.ApiKeys.AsNoTracking()
            where key.OAuthClientId != null
            join user in db.Users.AsNoTracking() on key.CreatedByUserId equals (Guid?)user.Id into users
            from user in users.DefaultIfEmpty()
            orderby key.CreatedAt descending
            select new { Key = key, Person = user == null ? null : user.DisplayName ?? user.Email })
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(rows.Select(r => ToSummary(r.Key, r.Person)).ToList());
    }

    // Revoking twice is not an error: the connection stays revoked from the first time. Its tokens stop working at once,
    // because authentication checks the connection's row.
    private static async Task<Results<Ok<OAuthConnectionSummary>, NotFound>> RevokeOAuthConnection(
        Guid connectionId, OAuthServer server, ScreenHireDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var key = await db.ApiKeys.SingleOrDefaultAsync(k => k.Id == connectionId && k.OAuthClientId != null, cancellationToken);
        if (key is null)
        {
            return TypedResults.NotFound();
        }

        var now = time.GetUtcNow();
        key.Revoke(now);
        await db.SaveChangesAsync(cancellationToken);
        await server.RevokeTokensAsync(key.Id, now, cancellationToken);

        var person = key.CreatedByUserId is { } id
            ? await db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => u.DisplayName ?? u.Email).SingleOrDefaultAsync(cancellationToken)
            : null;
        return TypedResults.Ok(ToSummary(key, person));
    }

    private static OAuthConnectionSummary ToSummary(ApiKey key, string? person) =>
        new(key.Id, key.Name, key.CreatedByUserId, person, key.Scopes, key.CreatedAt, key.LastUsedAt, key.RevokedAt);

    private static ProblemHttpResult CannotConnect(string detail) =>
        TypedResults.Problem(title: "This connection cannot be made", detail: detail, statusCode: StatusCodes.Status400BadRequest);
}
