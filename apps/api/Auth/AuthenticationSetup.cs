using System.Net;
using System.Reflection;
using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using ScreenHire.Api.Configuration;
using ScreenHire.Api.Contracts;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Identity;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Api.Auth;

/// <summary>
/// Sign-in for the recruiter dashboard: ASP.NET Core Identity as the user store, a cookie session, OpenID Connect
/// providers from configuration, emailed links, antiforgery, rate limits and CORS. Every route requires a signed-in
/// user unless it says <c>AllowAnonymous</c>.
/// </summary>
internal static class AuthenticationSetup
{
    /// <summary>The per-IP rate limit on sign-in endpoints that change state.</summary>
    public const string RateLimitPolicy = "auth";

    /// <summary>The per-IP rate limit on the anonymous passkey sign-in routes.</summary>
    public const string PasskeyRateLimitPolicy = "passkey";

    /// <summary>The session cookie.</summary>
    public const string SessionCookie = "sh.session";

    // The build writes openapi.json by starting the app inside this tool, with no configuration at all (Microsoft's
    // documented check for build-time document generation). Settings are validated when the app really runs.
    internal static readonly bool GeneratingOpenApiDocument = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

    public static WebApplicationBuilder AddScreenHireAuthentication(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var domainsOptions = services.AddOptions<DomainOptions>().BindConfiguration(DomainOptions.Section).ValidateDataAnnotations();
        var authOptionsBuilder = services.AddOptions<AuthOptions>().BindConfiguration(AuthOptions.Section).ValidateDataAnnotations();
        if (!GeneratingOpenApiDocument)
        {
            domainsOptions.ValidateOnStart();
            authOptionsBuilder.ValidateOnStart();
        }

        // Read now, not through IOptions: authentication schemes and cookie policies are fixed at startup.
        var domains = builder.Configuration.GetSection(DomainOptions.Section).Get<DomainOptions>() ?? new DomainOptions();
        var authOptions = builder.Configuration.GetSection(AuthOptions.Section).Get<AuthOptions>() ?? new AuthOptions();
        var secure = domains.Scheme == "https" ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                // The user name is the email address, which may contain any character an address allows.
                options.User.AllowedUserNameCharacters = "";
                // Not Stores.SchemaVersion = Version3: that would change the model of this one context (other Identity tables
                // too) and leave the migrator's, the design-time one and the tests' on version 1, none of which have this
                // service provider. ScreenHireDbContext maps user_passkeys itself, whatever the version.
            })
            .AddEntityFrameworkStores<ScreenHireDbContext>()
            // Named in full so the store reads and writes ApplicationUserPasskey, the row with LastUsedAt; the shortcut above
            // would pick Identity's plain passkey type, which the model does not hold.
            .AddUserStore<UserOnlyStore<
                ApplicationUser, ScreenHireDbContext, Guid, IdentityUserClaim<Guid>, IdentityUserLogin<Guid>, IdentityUserToken<Guid>,
                ApplicationUserPasskey>>()
            .AddSignInManager();

        var authentication = services.AddAuthentication(IdentityConstants.ApplicationScheme);
        authentication.AddIdentityCookies();
        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = SessionCookie;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = secure;
            options.Cookie.Domain = string.IsNullOrEmpty(domains.CookieDomain) ? null : domains.CookieDomain;
            options.ExpireTimeSpan = TimeSpan.FromDays(14);
            options.SlidingExpiration = true;
            // An API answers 401/403; the dashboard decides where to send the person.
            options.Events.OnRedirectToLogin = context => Status(context.Response, StatusCodes.Status401Unauthorized);
            options.Events.OnRedirectToAccessDenied = context => Status(context.Response, StatusCodes.Status403Forbidden);
        });
        services.ConfigureExternalCookie(options =>
        {
            options.Cookie.Name = "sh.external";
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = secure;
        });
        // The state of a passkey ceremony (its challenge) waits in this cookie between the options request and the
        // browser's answer. Host-only, five minutes (the Identity default), the same cookie rules as the session.
        services.Configure<CookieAuthenticationOptions>(IdentityConstants.TwoFactorUserIdScheme, options =>
        {
            options.Cookie.Name = "sh.passkey";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = secure;
        });
        // The relying party is the dashboard's host, from configuration: the browser reaches the API through the
        // dashboard's /v1 path, so the request's own host is not what the person's authenticator knows the site by.
        services.AddOptions<IdentityPasskeyOptions>().Configure<IOptions<DomainOptions>>((passkeys, configured) =>
        {
            var appOrigin = configured.Value.GetAppOrigin();
            passkeys.ServerDomain = appOrigin.Host;
            // The exact app origin, not the default's "any subdomain": a careers page on a sibling host is not the
            // dashboard and must never get an assertion for it. A cross-origin frame is refused too.
            passkeys.ValidateOrigin = context => ValueTask.FromResult(
                !context.CrossOrigin
                && string.Equals(context.Origin, appOrigin.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase));
            // The sign-in button sends no username, so the credential must be one the authenticator can find by itself.
            passkeys.ResidentKeyRequirement = "required";
            // A passkey is the only factor, so the device must have checked the person (a PIN, a fingerprint, a face), not
            // only that someone touched it. Platform authenticators and phones always do.
            passkeys.UserVerificationRequirement = "required";
        });

        var providers = ExternalProviders.FromOptions(authOptions, allowPlainHttp: builder.Environment.IsDevelopment());
        services.AddSingleton(providers);
        foreach (var provider in providers.All)
        {
            authentication.AddOpenIdConnect(provider.Id, provider.DisplayName,
                options => ConfigureProvider(options, provider, secure));
        }

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        services.AddAntiforgery(options =>
        {
            options.HeaderName = Antiforgery.HeaderName;
            options.Cookie.Name = "sh.antiforgery";
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = secure;
        });

        // Browsers reach the API through a proxy (the dashboard's /v1 path), so the connection's address is the proxy's.
        // The client's comes from X-Forwarded-For, believed only from loopback (the default) and the configured proxies;
        // trusting any sender would hand every request a fresh rate-limit bucket. A proxy is an address or a network in
        // CIDR form, because a platform's ingress has a range, not one address.
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
            options.ForwardLimit = authOptions.ForwardLimit;
            foreach (var proxy in authOptions.TrustedProxyEntries())
            {
                if (IPAddress.TryParse(proxy, out var address))
                {
                    options.KnownProxies.Add(address);
                }
                else if (System.Net.IPNetwork.TryParse(proxy, out var network))
                {
                    options.KnownIPNetworks.Add(network);
                }
                else
                {
                    throw new InvalidOperationException($"{AuthOptions.Section}:TrustedProxies: '{proxy}' is not an IP address or a CIDR network.");
                }
            }
        });

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(RateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = context.RequestServices.GetRequiredService<IOptions<AuthOptions>>().Value.PermitsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                }));
            // The two anonymous passkey routes: a guess is one request, so they get a lower limit of their own.
            options.AddPolicy(PasskeyRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = context.RequestServices.GetRequiredService<IOptions<AuthOptions>>().Value.PasskeyPermitsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                }));
        });

        // The dashboard calls the API same-origin through its /v1 path, so browsers need no CORS for it. This policy only
        // lets the dashboard origin call the API host directly.
        services.AddCors(cors =>
        {
            if (domains.AppHost.Length > 0) // empty only while the build generates the OpenAPI document
            {
                cors.AddDefaultPolicy(policy => policy
                    .WithOrigins(domains.GetAppOrigin().GetLeftPart(UriPartial.Authority))
                    .AllowCredentials()
                    .AllowAnyHeader()
                    .AllowAnyMethod());
            }
        });

        builder.AddScreenHireEmail(authOptions.MagicLink.From ?? $"ScreenHire <no-reply@{domains.BaseDomain}>");
        if (services.Any(service => service.ServiceType == typeof(IEmailSender)))
        {
            services.AddScoped<MagicLinks>();
        }

        services.AddSingleton<IValidator<PasskeySignInRequest>, PasskeySignInRequestValidator>();
        services.AddSingleton<IValidator<RegisterPasskeyRequest>, RegisterPasskeyRequestValidator>();
        services.AddSingleton<IValidator<MagicLinkRequest>, MagicLinkRequestValidator>();
        services.AddSingleton<IValidator<MagicLinkVerification>, MagicLinkVerificationValidator>();
        services.AddSingleton<IValidator<OnboardingRequest>, OnboardingRequestValidator>();
        services.AddSingleton<IValidator<InvitationAcceptance>, InvitationAcceptanceValidator>();
        services.AddSingleton<IValidator<InvitationLookup>, InvitationLookupValidator>();
        services.AddSingleton<IValidator<InvitationRequest>, InvitationRequestValidator>();
        services.AddSingleton<IValidator<ChangeMemberRoleRequest>, ChangeMemberRoleRequestValidator>();
        services.AddSingleton<IValidator<ChangeMemberJobAccessRequest>, ChangeMemberJobAccessRequestValidator>();
        services.AddSingleton<IValidator<HiringTeamRequest>, HiringTeamRequestValidator>();
        services.AddSingleton<IValidator<CreateRoleRequest>, CreateRoleRequestValidator>();
        services.AddSingleton<IValidator<UpdateRoleRequest>, UpdateRoleRequestValidator>();
        services.AddSingleton<IValidator<UpdateOrganizationRequest>, UpdateOrganizationRequestValidator>();

        return builder;
    }

    private static void ConfigureProvider(
        OpenIdConnectOptions options, ExternalProvider provider, CookieSecurePolicy secure)
    {
        // ExternalProviders.FromOptions already refused plain http outside Development.
        var plainHttp = provider.Authority.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

        options.SignInScheme = IdentityConstants.ExternalScheme;
        options.Authority = provider.Authority;
        options.RequireHttpsMetadata = !plainHttp; // only the local mock provider (Dev SSO) is http
        options.ClientId = provider.Client.ClientId;
        options.ClientSecret = provider.Client.ClientSecret;

        // Authorization code + PKCE, with the response in the query string: a top-level GET back to us, so SameSite=Lax
        // cookies (correlation, nonce) arrive without needing SameSite=None.
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.ResponseMode = OpenIdConnectResponseMode.Query;
        options.UsePkce = true;
        options.CallbackPath = provider.CallbackPath;
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.SaveTokens = false;
        options.MapInboundClaims = false; // keep the token's names: sub, email, email_verified, name, tid, xms_edov
        options.TokenValidationParameters.NameClaimType = "name";

        // Google and Microsoft put everything in the ID token; other providers may only answer at userinfo.
        options.GetClaimsFromUserInfoEndpoint = provider.Kind == ProviderKind.Generic;
        options.ClaimActions.MapUniqueJsonKey("email_verified", "email_verified");
        if (provider.Kind == ProviderKind.Microsoft)
        {
            options.TokenValidationParameters.IssuerValidator = MicrosoftIssuer.Validate;
        }

        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.CorrelationCookie.SecurePolicy = secure;
        options.NonceCookie.SameSite = SameSiteMode.Lax;
        options.NonceCookie.SecurePolicy = secure;

        options.Events = new OpenIdConnectEvents
        {
            // The browser reaches the API through the dashboard's /v1 path, so the provider must send it back there,
            // not to the API's own host.
            OnRedirectToIdentityProvider = context =>
            {
                context.ProtocolMessage.RedirectUri = Domains(context.HttpContext).AppUrl(provider.CallbackPath).ToString();
                return Task.CompletedTask;
            },
            // A cancelled consent screen or a bad response: back to the sign-in page with a reason, never an error page.
            OnRemoteFailure = context =>
            {
                context.Response.Redirect(Domains(context.HttpContext).AppUrl("/sign-in?error=provider").ToString());
                context.HandleResponse();
                return Task.CompletedTask;
            },
        };
    }

    private static DomainOptions Domains(HttpContext context) =>
        context.RequestServices.GetRequiredService<IOptions<DomainOptions>>().Value;

    private static Task Status(HttpResponse response, int statusCode)
    {
        response.StatusCode = statusCode;
        return Task.CompletedTask;
    }
}
