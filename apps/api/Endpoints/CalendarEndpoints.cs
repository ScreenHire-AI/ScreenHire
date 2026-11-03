using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Configuration;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.Scheduling;
using ScreenHire.Domain.Scheduling;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// A member's own calendar (docs/architecture/interview-scheduling.md): connecting Google, Outlook or the local sandbox
/// calendar through the provider's OAuth consent, seeing which account it is, and disconnecting. Signed-in people only,
/// and no permission: each route changes only the caller's row, like switching organization. With no provider
/// configured, <c>GET /v1/calendar</c> lists none and connecting is a 404.
/// </summary>
/// <remarks>
/// The OAuth round trip is written here rather than with ASP.NET Core's OAuth handler, for the reasons
/// <c>SlackEndpoints</c> gives: the handler signs a person in, while this stores tokens for later calls, and the local
/// sandbox has no OAuth server for it to talk to. Sign-in's own OIDC flow is untouched. The state is protected and
/// time-limited (<see cref="CalendarOAuthState"/>) and its nonce sits in a cookie, so a callback only works in the
/// browser that started it, for the person and organization that started it.
/// </remarks>
internal static partial class CalendarEndpoints
{
    /// <summary>The callback's path up to the provider. Also the nonce cookie's path, so one cookie serves every provider.</summary>
    public const string CallbackPrefix = "/v1/calendar/oauth/callback";

    /// <summary>The cookie holding the round trip's nonce, sent only to the callback.</summary>
    public const string StateCookie = "sh.calendar-oauth";

    public static string CallbackPath(CalendarProvider provider) => $"{CallbackPrefix}/{CalendarProviderNames.Slug(provider)}";

    public static IEndpointRouteBuilder MapCalendarEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/calendar").WithTags("Calendar").RequireAuthorization();
        group.MapGet("", GetCalendar).WithName("GetCalendar").Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/connect", StartCalendarConnect).WithName("StartCalendarConnect")
            .RequireAntiforgeryToken()
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapDelete("", DisconnectCalendar).WithName("DisconnectCalendar")
            .RequireAntiforgeryToken()
            .Produces(StatusCodes.Status401Unauthorized);

        // The provider sends the browser back here, a top-level GET. Anonymous so a session that ended meanwhile reaches
        // sign-in instead of a bare 401; everything it does checks the state against the signed-in person.
        app.MapGet($"{CallbackPrefix}/{{provider}}", CompleteCalendarOAuth).AllowAnonymous().ExcludeFromDescription();

        // The sandbox's own calendar: mapped only when it is on, so the API's description never lists them.
        if (app.ServiceProvider.GetRequiredService<IOptions<CalendarOptions>>().Value.Sandbox.Enabled)
        {
            app.MapGet(SandboxCalendarProvider.AuthorizePath, AuthorizeInSandbox).RequireAuthorization().ExcludeFromDescription();
            app.MapPost("/v1/calendar/sandbox/busy", AddSandboxBusy).RequireAuthorization().RequireAntiforgeryToken().ExcludeFromDescription();
        }

        return app;
    }

    private static async Task<Ok<CalendarStatus>> GetCalendar(
        ScreenHireDbContext db, ITenantContext tenant, CalendarAccess calendar, CancellationToken cancellationToken)
    {
        var providers = calendar.Options.AvailableProviders
            .Select(p => new CalendarProviderOption(p, CalendarProviderNames.DisplayName(p)))
            .ToList();
        var connection = tenant is { OrganizationId: not null, UserId: { } userId }
            ? await db.CalendarConnections.AsNoTracking().SingleOrDefaultAsync(c => c.UserId == userId, cancellationToken)
            : null;
        return TypedResults.Ok(new CalendarStatus(calendar.Options.Sandbox.Enabled, providers, connection?.ToAccount()));
    }

    private static Results<Ok<CalendarAuthorization>, NotFound, ProblemHttpResult> StartCalendarConnect(
        StartCalendarConnectRequest request,
        HttpContext context,
        ITenantContext tenant,
        CalendarAccess calendar,
        CalendarOAuthState states,
        IOptions<DomainOptions> domains)
    {
        if (NoOrganization(tenant) is { } refused)
        {
            return refused;
        }

        if (calendar.Find(request.Provider) is not { } provider)
        {
            return TypedResults.NotFound();
        }

        var app = domains.Value;
        var (state, nonce) = states.Issue(provider.Id, tenant.OrganizationId!.Value, tenant.UserId!.Value, ReturnUrls.Sanitize(request.ReturnUrl));
        context.Response.Cookies.Append(StateCookie, nonce, StateCookieOptions(app));
        return TypedResults.Ok(new CalendarAuthorization(provider.AuthorizeUrl(state, app.AppUrl(CallbackPath(provider.Id))).AbsoluteUri));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DisconnectCalendar(
        ScreenHireDbContext db,
        ITenantContext tenant,
        CalendarAccess calendar,
        CalendarTokens tokens,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        if (NoOrganization(tenant) is { } refused)
        {
            return refused;
        }

        if (await db.CalendarConnections.SingleOrDefaultAsync(c => c.UserId == tenant.UserId, cancellationToken) is not { } connection)
        {
            return TypedResults.NoContent();
        }

        // Best effort: the calendar goes from ScreenHire either way, and a token the provider already dropped cannot be revoked.
        if (calendar.Find(connection.Provider) is { } provider
            && tokens.Unprotect(connection.OrganizationId, connection.UserId, connection.ProtectedRefreshToken) is { Length: > 0 } token)
        {
            try
            {
                await provider.RevokeAsync(token, cancellationToken);
            }
            catch (CalendarApiException e)
            {
                LogRevokeFailed(loggers.CreateLogger(typeof(CalendarEndpoints)), connection.OrganizationId, connection.Provider, e.Code);
            }
        }

        db.CalendarConnections.Remove(connection);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Where a provider sends the browser back. Every outcome is a redirect to the dashboard with <c>?calendar=</c>:
    /// <c>connected</c>, <c>denied</c> (cancelled on the provider's page), <c>expired</c> (the state no longer reads) or
    /// <c>failed</c>.
    /// </summary>
    private static async Task<RedirectHttpResult> CompleteCalendarOAuth(
        string provider,
        string? code,
        string? state,
        string? error,
        HttpContext context,
        ScreenHireDbContext db,
        ITenantContext tenant,
        CalendarAccess calendar,
        CalendarOAuthState states,
        CalendarTokens tokens,
        IOptions<DomainOptions> domains,
        TimeProvider time,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        var app = domains.Value;
        var nonce = context.Request.Cookies[StateCookie];
        context.Response.Cookies.Delete(StateCookie, StateCookieOptions(app));
        if (tenant.UserId is null)
        {
            return TypedResults.Redirect(app.AppUrl($"/sign-in?returnUrl={Uri.EscapeDataString(ReturnUrls.Default)}").AbsoluteUri);
        }

        var round = states.Read(state);
        if (round is null)
        {
            return Outcome(app, ReturnUrls.Default, "expired");
        }

        if (nonce != round.Nonce || round.UserId != tenant.UserId || round.OrganizationId != tenant.OrganizationId
            || !CalendarProviderNames.TryParse(provider, out var routed) || routed != round.Provider || calendar.Find(round.Provider) is not { } found)
        {
            return Outcome(app, round.ReturnUrl, "failed");
        }

        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
        {
            return Outcome(app, round.ReturnUrl, error == "access_denied" ? "denied" : "failed");
        }

        var logger = loggers.CreateLogger(typeof(CalendarEndpoints));
        var now = time.GetUtcNow();
        try
        {
            var grant = await found.ExchangeCodeAsync(code, app.AppUrl(CallbackPath(found.Id)), cancellationToken);
            var access = tokens.Protect(round.OrganizationId, round.UserId, grant.AccessToken);
            var refresh = tokens.Protect(round.OrganizationId, round.UserId, grant.RefreshToken ?? "");
            if (access.Length > CalendarConnection.MaxTokenLength || refresh.Length > CalendarConnection.MaxTokenLength)
            {
                LogConnectFailed(logger, round.OrganizationId, found.Id, "token_too_long");
                return Outcome(app, round.ReturnUrl, "failed");
            }

            if (await db.CalendarConnections.SingleOrDefaultAsync(c => c.UserId == round.UserId, cancellationToken) is { } connection)
            {
                connection.Reconnect(found.Id, grant.AccountEmail, access, refresh, grant.ExpiresAt, now);
            }
            else
            {
                db.CalendarConnections.Add(CalendarConnection.Connect(
                    round.OrganizationId, round.UserId, found.Id, grant.AccountEmail, access, refresh, grant.ExpiresAt, now));
            }

            await db.SaveChangesAsync(cancellationToken);
            return Outcome(app, round.ReturnUrl, "connected");
        }
        catch (CalendarApiException e)
        {
            LogConnectFailed(logger, round.OrganizationId, found.Id, e.Code);
            return Outcome(app, round.ReturnUrl, "failed");
        }
        catch (DbUpdateException)
        {
            // Two round trips finished at once and the other one saved first (one calendar per member).
            LogConnectFailed(logger, round.OrganizationId, found.Id, "concurrent_save");
            return Outcome(app, round.ReturnUrl, "failed");
        }
    }

    // The sandbox's consent page: none. It sends the browser straight back to our own callback with a code, as a provider
    // would after someone clicked Allow. Only to our own sandbox callback, so it is never an open redirect.
    private static Results<RedirectHttpResult, BadRequest> AuthorizeInSandbox(
        string? state, [FromQuery(Name = "redirect_uri")] string? redirectUri, ITenantContext tenant, IOptions<DomainOptions> domains)
    {
        var callback = domains.Value.AppUrl(CallbackPath(CalendarProvider.Sandbox));
        if (redirectUri != callback.ToString() || state is null || tenant.UserId is not { } userId)
        {
            return TypedResults.BadRequest();
        }

        var code = $"{SandboxCalendarProvider.ConnectCodePrefix}{userId:N}";
        return TypedResults.Redirect($"{callback}?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(state)}");
    }

    // A development and test aid: marks the caller busy in the sandbox calendar, so a test can take a slot away.
    private static async Task<Results<NoContent, ValidationProblem, ProblemHttpResult>> AddSandboxBusy(
        AddSandboxBusyRequest request, ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken)
    {
        if (NoOrganization(tenant) is { } refused)
        {
            return refused;
        }

        if (request.End <= request.Start || request.End - request.Start > TimeSpan.FromDays(31))
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("end", "End must be after start, within 31 days."));
        }

        db.CalendarSandboxBusy.Add(new CalendarSandboxBusy
        {
            OrganizationId = tenant.OrganizationId!.Value,
            UserId = tenant.UserId!.Value,
            StartsAt = request.Start.ToUniversalTime(),
            EndsAt = request.End.ToUniversalTime(),
        });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    // Lax, so the cookie arrives with the provider's top-level redirect back; only the callback reads it.
    private static CookieOptions StateCookieOptions(DomainOptions app) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Secure = app.Scheme == "https",
        Path = CallbackPrefix,
        MaxAge = CalendarOAuthState.Lifetime,
    };

    private static RedirectHttpResult Outcome(DomainOptions app, string returnUrl, string outcome)
    {
        var path = ReturnUrls.Sanitize(returnUrl);
        var separator = path.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return TypedResults.Redirect(app.AppUrl($"{path}{separator}calendar={outcome}").AbsoluteUri);
    }

    private static ProblemHttpResult? NoOrganization(ITenantContext tenant) =>
        tenant is { OrganizationId: not null, UserId: not null }
            ? null
            : TypedResults.Problem(title: "No organization", detail: "Create or join an organization first.", statusCode: StatusCodes.Status403Forbidden);

    private static CalendarAccount ToAccount(this CalendarConnection connection) =>
        new(connection.Provider, CalendarProviderNames.DisplayName(connection.Provider), connection.AccountEmail, connection.ConnectedAt, connection.Problem);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Revoking a {Provider} calendar token for organization {OrganizationId} failed: {Code}")]
    private static partial void LogRevokeFailed(ILogger logger, Guid organizationId, CalendarProvider provider, string code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Connecting a {Provider} calendar for organization {OrganizationId} failed: {Code}")]
    private static partial void LogConnectFailed(ILogger logger, Guid organizationId, CalendarProvider provider, string code);
}
