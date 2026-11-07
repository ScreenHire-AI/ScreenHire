using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Configuration;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.OAuth;

/// <summary>
/// Authenticates <c>Authorization: Bearer sho_...</c>, an access token an OAuth client got for a connection a person
/// approved (ADR-0027). Only the MCP endpoint reaches it (<see cref="ApiKeyAuthentication.McpCredentialsScheme"/>). It
/// works like the API key handler: the token names its organization, which becomes the tenant in a scope of its own before
/// the lookup, so the lookup runs under row-level security. The principal it returns is the connection's key row, so
/// scopes, the rate limit, the timeline and the verification review log treat the connection exactly as a key. A bad or
/// expired token fails and counts against the client address.
/// </summary>
internal sealed class OAuthAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IServiceScopeFactory scopes,
    IOptions<ApiKeyOptions> keyOptions,
    IOptions<DomainOptions> domains,
    ApiKeyAuthentication.FailedAttempts failures,
    TimeProvider time)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private bool tooManyFailures;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var address = Context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (failures.Exhausted(address))
        {
            tooManyFailures = true;
            return AuthenticateResult.Fail("Too many failed attempts from this address.");
        }

        AuthenticateResult Failed(string reason)
        {
            failures.Record(address);
            return AuthenticateResult.Fail(reason);
        }

        var token = ApiKeyAuthentication.BearerToken(Request);
        if (!OAuthTokens.TryReadOrganization(OAuthTokens.AccessMarker, token, out var organizationId))
        {
            return Failed("Not a ScreenHire access token.");
        }

        // In a scope of its own, so the request's tenant is set only from a token that authenticated
        // (TenantResolutionMiddleware), never from the id an unchecked token names.
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetOrganization(organizationId.Value);
        var db = scope.ServiceProvider.GetRequiredService<ScreenHireDbContext>();
        var hash = OAuthTokens.Hash(token!);
        var row = await (
            from t in db.OAuthTokens.AsNoTracking()
            join k in db.ApiKeys.AsNoTracking() on t.ApiKeyId equals k.Id
            where t.TokenHash == hash && t.Kind == OAuthTokenKind.Access
            select new
            {
                t.OrganizationId,
                t.ExpiresAt,
                t.RevokedAt,
                KeyId = k.Id,
                k.Scopes,
                k.PermitsPerMinute,
                k.LastUsedAt,
                KeyRevokedAt = k.RevokedAt,
                k.OAuthClientId,
            })
            .SingleOrDefaultAsync(Context.RequestAborted);

        var now = time.GetUtcNow();
        if (row is null || row.OrganizationId != organizationId || row.OAuthClientId is null
            || row.RevokedAt is not null || row.KeyRevokedAt is not null || row.ExpiresAt <= now)
        {
            return Failed("Unknown, expired or revoked access token.");
        }

        await ApiKeyAuthentication.TouchAsync(db, row.KeyId, row.LastUsedAt, now, Context.RequestAborted);

        var principal = ApiKeyAuthentication.KeyPrincipal(
            row.KeyId, row.OrganizationId, ApiKeyAuthentication.PermitsFor(row.PermitsPerMinute, keyOptions.Value), row.Scopes);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (tooManyFailures)
        {
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            Response.Headers.RetryAfter = "60";
            return Task.CompletedTask;
        }

        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = ApiKeyAuthentication.McpChallenge(domains.Value, sentCredentials: true);
        return Task.CompletedTask;
    }
}
