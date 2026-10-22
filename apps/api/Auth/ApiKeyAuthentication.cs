using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Configuration;
using ScreenHire.Api.Contracts;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Auth;

/// <summary>
/// Organization API keys on the hiring API (ADR-0011): an ASP.NET Core authentication scheme for
/// <c>Authorization: Bearer shk_...</c>, the <see cref="HiringApiPolicy"/> that lets it in beside the session cookie, and
/// a per-key rate limit. Every other route keeps the cookie-only default and fallback policies, so a key gets 401 on
/// sign-in, onboarding, key management and everything outside the hiring API.
/// </summary>
internal static class ApiKeyAuthentication
{
    /// <summary>The API key authentication scheme.</summary>
    public const string Scheme = "ApiKey";

    /// <summary>
    /// The hiring API's scheme: the API key when the request has an <c>Authorization</c> header, the session cookie
    /// otherwise. Never both, so a request is a key or a session and nothing in between.
    /// </summary>
    public const string SessionOrApiKeyScheme = "SessionOrApiKey";

    /// <summary>The authorization policy of the routes keys may call: jobs, applications, candidates and templates.</summary>
    public const string HiringApiPolicy = "hiring-api";

    /// <summary>The per-key rate limit. Sessions pass unlimited.</summary>
    public const string RateLimitPolicy = "api-key";

    private const string KeyIdClaim = "sh_api_key";
    private const string OrganizationClaim = "sh_organization";
    private const string ScopeClaim = "scope";
    private const string PermitsClaim = "sh_permits_per_minute";

    public static WebApplicationBuilder AddScreenHireApiKeys(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var options = services.AddOptions<ApiKeyOptions>().BindConfiguration(ApiKeyOptions.Section).ValidateDataAnnotations();
        if (!AuthenticationSetup.GeneratingOpenApiDocument)
        {
            options.ValidateOnStart();
        }

        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(Scheme, displayName: "API key", _ => { })
            .AddPolicyScheme(SessionOrApiKeyScheme, displayName: null, policy => policy.ForwardDefaultSelector = context =>
                context.Request.Headers.Authorization.Count > 0 ? Scheme : IdentityConstants.ApplicationScheme);

        services.AddAuthorizationBuilder().AddPolicy(HiringApiPolicy, policy => policy
            .AddAuthenticationSchemes(SessionOrApiKeyScheme)
            .RequireAuthenticatedUser()
            .RequireAssertion(context => !context.User.IsApiKey() || context.User.HasApiKeyScope(ApiKeyScope.Read)));

        services.AddRateLimiter(limiter => limiter.AddPolicy(RateLimitPolicy, context =>
            context.User.IsApiKey()
                ? RateLimitPartition.GetFixedWindowLimiter(
                    context.User.FindFirstValue(KeyIdClaim)!,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = int.Parse(context.User.FindFirstValue(PermitsClaim)!, CultureInfo.InvariantCulture),
                        Window = TimeSpan.FromMinutes(1),
                    })
                : RateLimitPartition.GetNoLimiter("session")));

        services.AddSingleton<IValidator<CreateApiKeyRequest>, CreateApiKeyRequestValidator>();
        return builder;
    }

    /// <summary>Opens a route group to API keys: the <see cref="HiringApiPolicy"/> and the per-key rate limit.</summary>
    public static RouteGroupBuilder AllowApiKeys(this RouteGroupBuilder group) =>
        group.RequireAuthorization(HiringApiPolicy).RequireRateLimiting(RateLimitPolicy);

    /// <summary>
    /// Whether <paramref name="user"/> was authenticated by an API key. Decided by the identity's authentication type,
    /// which only <see cref="ApiKeyAuthenticationHandler"/> sets to <see cref="Scheme"/>; claims alone prove nothing.
    /// </summary>
    public static bool IsApiKey(this ClaimsPrincipal user) =>
        user.Identity is { IsAuthenticated: true, AuthenticationType: Scheme };

    /// <summary>The organization of an API key's request, or null for anything else.</summary>
    public static Guid? ApiKeyOrganizationId(this ClaimsPrincipal user) =>
        user.IsApiKey() && Guid.TryParse(user.FindFirstValue(OrganizationClaim), out var id) ? id : null;

    public static bool HasApiKeyScope(this ClaimsPrincipal user, ApiKeyScope scope) =>
        user.IsApiKey() && ApiKey.Allows(
            user.FindAll(ScopeClaim).Select(c => Enum.TryParse<ApiKeyScope>(c.Value, out var s) ? s : (ApiKeyScope?)null).OfType<ApiKeyScope>(),
            scope);

    /// <summary>
    /// Authenticates <c>Authorization: Bearer shk_...</c>. A request without the header is no result; a malformed,
    /// unknown or revoked key fails, which the hiring API answers with 401. The key never reaches a log: failure messages
    /// name the reason only.
    /// </summary>
    private sealed class ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IServiceScopeFactory scopes,
        IOptions<ApiKeyOptions> keyOptions,
        TimeProvider time)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        // last_used_at is written at most once a minute per key, not on every request.
        private static readonly TimeSpan LastUsedResolution = TimeSpan.FromMinutes(1);

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!AuthenticationHeaderValue.TryParse(Request.Headers.Authorization.ToString(), out var header))
            {
                return Request.Headers.Authorization.Count == 0 ? AuthenticateResult.NoResult() : AuthenticateResult.Fail("Malformed Authorization header.");
            }

            if (!"Bearer".Equals(header.Scheme, StringComparison.OrdinalIgnoreCase) || header.Parameter is not { } token)
            {
                return AuthenticateResult.Fail("Only Bearer API keys are accepted.");
            }

            if (!ApiKeyTokens.TryReadOrganization(token, out var organizationId))
            {
                return AuthenticateResult.Fail("Not a ScreenHire API key.");
            }

            // In a scope of its own, so the request's tenant is set only from a key that authenticated
            // (TenantResolutionMiddleware), never from the id an unchecked key names.
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<TenantContext>().SetOrganization(organizationId.Value);
            var db = scope.ServiceProvider.GetRequiredService<ScreenHireDbContext>();
            var hash = ApiKeyTokens.Hash(token);
            var key = await db.ApiKeys.AsNoTracking()
                .Where(k => k.SecretHash == hash)
                .Select(k => new { k.Id, k.OrganizationId, k.Scopes, k.PermitsPerMinute, k.LastUsedAt, k.RevokedAt })
                .SingleOrDefaultAsync(Context.RequestAborted);
            if (key is null || key.OrganizationId != organizationId || key.RevokedAt is not null)
            {
                return AuthenticateResult.Fail("Unknown or revoked API key.");
            }

            var now = time.GetUtcNow();
            if (key.LastUsedAt is null || key.LastUsedAt < now - LastUsedResolution)
            {
                var since = now - LastUsedResolution;
                await db.ApiKeys
                    .Where(k => k.Id == key.Id && (k.LastUsedAt == null || k.LastUsedAt < since))
                    .ExecuteUpdateAsync(set => set.SetProperty(k => k.LastUsedAt, now), Context.RequestAborted);
            }

            var permits = key.PermitsPerMinute ?? keyOptions.Value.PermitsPerMinute;
            List<Claim> claims =
            [
                new(KeyIdClaim, key.Id.ToString()),
                new(OrganizationClaim, key.OrganizationId.ToString()),
                new(PermitsClaim, permits.ToString(CultureInfo.InvariantCulture)),
                .. key.Scopes.Select(s => new Claim(ScopeClaim, s.ToString())),
            ];
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
            return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            Response.Headers.WWWAuthenticate = "Bearer";
            return Task.CompletedTask;
        }
    }
}
