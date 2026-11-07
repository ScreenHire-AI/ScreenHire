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
using ScreenHire.Api.OAuth;
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

    /// <summary>The scheme for an OAuth access token (ADR-0027). Registered, but only <see cref="McpCredentialsScheme"/> reaches it.</summary>
    public const string OAuthScheme = "OAuth";

    /// <summary>
    /// The MCP endpoint's scheme: the OAuth handler for an <c>sho_</c> access token, the API key handler for anything else.
    /// The hiring routes do not use it, so an OAuth token works on <c>/v1/mcp</c> and nowhere else (the spec's audience rule).
    /// </summary>
    public const string McpCredentialsScheme = "McpCredentials";

    /// <summary>The authorization policy of the routes keys may call: jobs, applications, candidates, templates and reports.</summary>
    public const string HiringApiPolicy = "hiring-api";

    /// <summary>
    /// The session, or an API key with the Verification read scope (ADR-0021): the policy of the routes that read an
    /// application's verification. A key without the scope is refused, so verification stays out of every other key.
    /// </summary>
    public const string VerificationApiPolicy = "verification-api";

    /// <summary>The per-key rate limit. Sessions pass unlimited.</summary>
    public const string RateLimitPolicy = "api-key";

    private const string KeyIdClaim = "sh_api_key";
    private const string OrganizationClaim = "sh_organization";
    private const string ScopeClaim = "scope";
    private const string PermitsClaim = "sh_permits_per_minute";

    // last_used_at is written at most once a minute per key, not on every request.
    private static readonly TimeSpan LastUsedResolution = TimeSpan.FromMinutes(1);

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
            .AddScheme<AuthenticationSchemeOptions, OAuthAuthenticationHandler>(OAuthScheme, displayName: "OAuth access token", _ => { })
            .AddPolicyScheme(SessionOrApiKeyScheme, displayName: null, policy => policy.ForwardDefaultSelector = context =>
                context.Request.Headers.Authorization.Count > 0 ? Scheme : IdentityConstants.ApplicationScheme)
            .AddPolicyScheme(McpCredentialsScheme, displayName: null, policy => policy.ForwardDefaultSelector = context =>
                OAuthTokens.LooksLikeAccessToken(BearerToken(context.Request)) ? OAuthScheme : Scheme);

        services.AddAuthorizationBuilder().AddPolicy(HiringApiPolicy, policy => policy
            .AddAuthenticationSchemes(SessionOrApiKeyScheme)
            .RequireAuthenticatedUser()
            .RequireAssertion(context => !context.User.IsApiKey() || context.User.HasApiKeyScope(ApiKeyScope.Read)));

        services.AddAuthorizationBuilder().AddPolicy(VerificationApiPolicy, policy => policy
            .AddAuthenticationSchemes(SessionOrApiKeyScheme)
            .RequireAuthenticatedUser()
            .RequireAssertion(context => !context.User.IsApiKey() || context.User.HasApiKeyScope(ApiKeyScope.VerificationRead)));

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

        services.AddSingleton<FailedAttempts>();
        services.AddSingleton<IValidator<CreateApiKeyRequest>, CreateApiKeyRequestValidator>();
        return builder;
    }

    /// <summary>
    /// Opens a route group to API keys: the <see cref="HiringApiPolicy"/> and the per-key rate limit, whose 429 the OpenAPI
    /// document declares on every route of the group.
    /// </summary>
    public static RouteGroupBuilder AllowApiKeys(this RouteGroupBuilder group) =>
        group.RequireAuthorization(HiringApiPolicy).RequireRateLimiting(RateLimitPolicy)
            .WithMetadata(new ProducesResponseTypeMetadata(StatusCodes.Status429TooManyRequests, typeof(void))); // Produces() is for one route only

    /// <summary>
    /// Opens a route group to an API key that has the Verification read scope, and to the session: the
    /// <see cref="VerificationApiPolicy"/> and the per-key rate limit. Its routes only read; every read through a key is
    /// recorded in the verification review log (ADR-0021).
    /// </summary>
    public static RouteGroupBuilder AllowVerificationApiKeys(this RouteGroupBuilder group) =>
        group.RequireAuthorization(VerificationApiPolicy).RequireRateLimiting(RateLimitPolicy)
            .WithMetadata(new ProducesResponseTypeMetadata(StatusCodes.Status429TooManyRequests, typeof(void)));

    /// <summary>
    /// Whether <paramref name="user"/> was authenticated by an API key. Decided by the identity's authentication type,
    /// which only <see cref="ApiKeyAuthenticationHandler"/> sets to <see cref="Scheme"/>; claims alone prove nothing.
    /// </summary>
    public static bool IsApiKey(this ClaimsPrincipal user) =>
        user.Identity is { IsAuthenticated: true, AuthenticationType: Scheme };

    /// <summary>The organization of an API key's request, or null for anything else.</summary>
    public static Guid? ApiKeyOrganizationId(this ClaimsPrincipal user) =>
        user.IsApiKey() && Guid.TryParse(user.FindFirstValue(OrganizationClaim), out var id) ? id : null;

    /// <summary>The API key of the request, for the audit trail, or null for anything else.</summary>
    public static Guid? ApiKeyId(this ClaimsPrincipal user) =>
        user.IsApiKey() && Guid.TryParse(user.FindFirstValue(KeyIdClaim), out var id) ? id : null;

    public static bool HasApiKeyScope(this ClaimsPrincipal user, ApiKeyScope scope) =>
        user.IsApiKey() && ApiKey.Allows(
            user.FindAll(ScopeClaim).Select(c => Enum.TryParse<ApiKeyScope>(c.Value, out var s) ? s : (ApiKeyScope?)null).OfType<ApiKeyScope>(),
            scope);

    /// <summary>The bearer token of the request, or null when there is no well-formed <c>Authorization: Bearer</c> header.</summary>
    internal static string? BearerToken(HttpRequest request) =>
        AuthenticationHeaderValue.TryParse(request.Headers.Authorization.ToString(), out var header)
        && "Bearer".Equals(header.Scheme, StringComparison.OrdinalIgnoreCase)
            ? header.Parameter
            : null;

    /// <summary>
    /// The 401 challenge of the MCP endpoint: where its protected resource metadata is (RFC 9728), which is how an OAuth
    /// client finds the authorization server. A request that sent a credential also gets <c>invalid_token</c> (RFC 6750).
    /// </summary>
    internal static string McpChallenge(DomainOptions domains, bool sentCredentials) =>
        (sentCredentials ? "Bearer error=\"invalid_token\", " : "Bearer ")
        + $"resource_metadata=\"{OAuth.OAuthMetadata.ProtectedResourceUrl(domains)}\"";

    /// <summary>
    /// The principal of a key or an OAuth connection: one claim set, with the authentication type of the API key scheme, so
    /// tenancy, the rate limit, the scope checks and the audit trail treat both alike.
    /// </summary>
    internal static ClaimsPrincipal KeyPrincipal(Guid keyId, Guid organizationId, int permitsPerMinute, IEnumerable<ApiKeyScope> scopes)
    {
        List<Claim> claims =
        [
            new(KeyIdClaim, keyId.ToString()),
            new(OrganizationClaim, organizationId.ToString()),
            new(PermitsClaim, permitsPerMinute.ToString(CultureInfo.InvariantCulture)),
            .. scopes.Select(s => new Claim(ScopeClaim, s.ToString())),
        ];
        return new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme));
    }

    /// <summary>Records that the key authenticated, at most once a minute.</summary>
    internal static async Task TouchAsync(
        ScreenHireDbContext db, Guid keyId, DateTimeOffset? lastUsedAt, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (lastUsedAt is null || lastUsedAt < now - LastUsedResolution)
        {
            var since = now - LastUsedResolution;
            await db.ApiKeys
                .Where(k => k.Id == keyId && (k.LastUsedAt == null || k.LastUsedAt < since))
                .ExecuteUpdateAsync(set => set.SetProperty(k => k.LastUsedAt, now), cancellationToken);
        }
    }

    /// <summary>The key's rate limit. The deployment's limit is also the most any key gets, including one created before it was lowered.</summary>
    internal static int PermitsFor(int? keyPermits, ApiKeyOptions options) => Math.Min(keyPermits ?? options.PermitsPerMinute, options.PermitsPerMinute);

    /// <summary>
    /// Failed key attempts per client address, <c>ScreenHire:ApiKeys:FailedAttemptsPerMinute</c> in a fixed one-minute
    /// window. An address that runs out gets 429 for every key until the window ends, without a database lookup. The OAuth
    /// handler counts its failures here too.
    /// </summary>
    /// <remarks>
    /// The rate limiter middleware cannot do this: it runs after authorization, which has already answered 401 to a
    /// failed key. So the handler holds the limiter itself.
    /// </remarks>
    internal sealed class FailedAttempts : IDisposable
    {
        private readonly PartitionedRateLimiter<string> limiter;

        public FailedAttempts(IOptions<ApiKeyOptions> options) =>
            limiter = PartitionedRateLimiter.Create<string, string>(address => RateLimitPartition.GetFixedWindowLimiter(
                address,
                _ => new FixedWindowRateLimiterOptions { PermitLimit = options.Value.FailedAttemptsPerMinute, Window = TimeSpan.FromMinutes(1) }));

        public bool Exhausted(string address) => limiter.GetStatistics(address) is { CurrentAvailablePermits: <= 0 };

        public void Record(string address)
        {
            using var lease = limiter.AttemptAcquire(address);
        }

        public void Dispose() => limiter.Dispose();
    }

    /// <summary>
    /// Authenticates <c>Authorization: Bearer shk_...</c>. A request without the header is no result; a malformed,
    /// unknown or revoked key fails, which the hiring API answers with 401, and counts against the client address
    /// (<see cref="FailedAttempts"/>). The key never reaches a log: failure messages name the reason only.
    /// </summary>
    private sealed class ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IServiceScopeFactory scopes,
        IOptions<ApiKeyOptions> keyOptions,
        IOptions<DomainOptions> domains,
        FailedAttempts failures,
        TimeProvider time)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        private bool tooManyFailures;

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Headers.Authorization.Count == 0)
            {
                return AuthenticateResult.NoResult();
            }

            // UseForwardedHeaders has already put the client's address here.
            var address = Context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            if (failures.Exhausted(address))
            {
                tooManyFailures = true;
                return AuthenticateResult.Fail("Too many failed API key attempts from this address.");
            }

            AuthenticateResult Failed(string reason)
            {
                failures.Record(address);
                return AuthenticateResult.Fail(reason);
            }

            if (!AuthenticationHeaderValue.TryParse(Request.Headers.Authorization.ToString(), out var header))
            {
                return Failed("Malformed Authorization header.");
            }

            if (!"Bearer".Equals(header.Scheme, StringComparison.OrdinalIgnoreCase) || header.Parameter is not { } token)
            {
                return Failed("Only Bearer API keys are accepted.");
            }

            if (!ApiKeyTokens.TryReadOrganization(token, out var organizationId))
            {
                return Failed("Not a ScreenHire API key.");
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
                return Failed("Unknown or revoked API key.");
            }

            await TouchAsync(db, key.Id, key.LastUsedAt, time.GetUtcNow(), Context.RequestAborted);

            var principal = KeyPrincipal(key.Id, key.OrganizationId, PermitsFor(key.PermitsPerMinute, keyOptions.Value), key.Scopes);
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
            Response.Headers.WWWAuthenticate = Request.Path.StartsWithSegments(Mcp.McpSetup.Path)
                ? McpChallenge(domains.Value, sentCredentials: Request.Headers.Authorization.Count > 0)
                : "Bearer";
            return Task.CompletedTask;
        }
    }
}
