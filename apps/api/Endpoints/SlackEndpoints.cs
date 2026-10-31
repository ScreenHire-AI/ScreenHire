using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Configuration;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.Slack;
using ScreenHire.Domain.Organizations;
using ScreenHire.Domain.Slack;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// Slack (docs/architecture/slack.md): connecting the organization's workspace through Slack's OAuth install, the
/// channel and the events posted there, and each member's own account, linked with Sign in with Slack. Signed-in people
/// only: an API key gets nothing here. With no Slack app configured, every route but <c>GET /v1/slack</c> is a 404.
/// </summary>
/// <remarks>
/// The OAuth round trips are written here rather than with ASP.NET Core's OAuth handler: that handler signs a person in,
/// while an install stores a workspace's bot token, and the local sandbox has no OAuth server for it to talk to. The
/// state is protected and time-limited (<see cref="SlackOAuthState"/>) and its nonce sits in a cookie, so a callback only
/// works in the browser that started it, for the person and organization that started it.
/// </remarks>
internal static partial class SlackEndpoints
{
    public const string CallbackPath = "/v1/slack/oauth/callback";

    /// <summary>The cookie holding the round trip's nonce, sent only to the callback.</summary>
    public const string StateCookie = "sh.slack-oauth";

    public const string SettingsPath = "/settings/slack";

    public static IEndpointRouteBuilder MapSlackEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/slack").WithTags("Slack").RequireAuthorization();
        group.MapGet("", GetSlack).WithName("GetSlack").Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/install", StartSlackInstall).WithName("StartSlackInstall")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.ManageIntegrations)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/channels", ListSlackChannels).WithName("ListSlackChannels")
            .RequirePermission(Permission.ManageIntegrations)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPut("/channel", UpdateSlackChannel).WithName("UpdateSlackChannel")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.ManageIntegrations)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/test", SendSlackTestMessage).WithName("SendSlackTestMessage")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.ManageIntegrations)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapDelete("", DisconnectSlack).WithName("DisconnectSlack")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.ManageIntegrations)
            .Produces(StatusCodes.Status401Unauthorized);

        // A member's own account: no permission, like switching organization, because each changes only the caller's row.
        group.MapPost("/account/link", StartSlackLink).WithName("StartSlackLink")
            .RequireAntiforgeryToken()
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPut("/account", UpdateSlackAccount).WithName("UpdateSlackAccount")
            .RequireAntiforgeryToken()
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapDelete("/account", UnlinkSlackAccount).WithName("UnlinkSlackAccount")
            .RequireAntiforgeryToken()
            .Produces(StatusCodes.Status401Unauthorized);

        // Slack sends the browser back here, a top-level GET. Anonymous so a session that ended meanwhile reaches sign-in
        // instead of a bare 401; everything it does checks the state against the signed-in person.
        app.MapGet(CallbackPath, CompleteSlackOAuth).AllowAnonymous().ExcludeFromDescription();

        // The sandbox's own "Slack": mapped only when it is on, so the API's description never lists them.
        if (app.ServiceProvider.GetRequiredService<IOptions<SlackOptions>>().Value.Mode == SlackMode.Sandbox)
        {
            app.MapGet(SandboxSlackClient.AuthorizePath, AuthorizeInSandbox).RequireAuthorization().ExcludeFromDescription();
            app.MapGet("/v1/slack/sandbox/messages", ListSandboxMessages).RequireAuthorization().ExcludeFromDescription();
        }

        return app;
    }

    private static async Task<Ok<SlackStatus>> GetSlack(
        ScreenHireDbContext db, ITenantContext tenant, SlackAccess slack, CancellationToken cancellationToken) =>
        TypedResults.Ok(await StatusAsync(db, tenant, slack, cancellationToken));

    private static Results<Ok<SlackAuthorization>, NotFound> StartSlackInstall(
        HttpContext context, ITenantContext tenant, SlackAccess slack, SlackOAuthState states, IOptions<DomainOptions> domains)
    {
        if (!slack.IsConfigured)
        {
            return TypedResults.NotFound();
        }

        var state = Issue(context, states, domains.Value, SlackOAuthPurpose.Install, tenant, SettingsPath);
        return TypedResults.Ok(new SlackAuthorization(slack.Client.InstallUrl(state, domains.Value.AppUrl(CallbackPath)).AbsoluteUri));
    }

    private static async Task<Results<Ok<List<SlackChannelOption>>, NotFound, ProblemHttpResult>> ListSlackChannels(
        ScreenHireDbContext db, SlackAccess slack, SlackTokens tokens, TimeProvider time, CancellationToken cancellationToken)
    {
        if (!slack.IsConfigured || await db.SlackInstallations.SingleOrDefaultAsync(cancellationToken) is not { } installation)
        {
            return TypedResults.NotFound();
        }

        if (tokens.Unprotect(installation.OrganizationId, installation.ProtectedBotToken) is not { } token)
        {
            return await DisconnectedAsync(db, installation, time, cancellationToken);
        }

        try
        {
            var channels = await slack.Client.ListChannelsAsync(token, cancellationToken);
            return TypedResults.Ok(channels.OrderBy(c => c.Name, StringComparer.Ordinal).Select(c => new SlackChannelOption(c.Id, c.Name, c.IsPrivate)).ToList());
        }
        catch (SlackApiException e) when (e.Problem == SlackProblem.Disconnected)
        {
            return await DisconnectedAsync(db, installation, time, cancellationToken);
        }
        catch (SlackApiException)
        {
            return SlackDidNotAnswer();
        }
    }

    private static async Task<Results<Ok<SlackStatus>, NotFound, ValidationProblem, ProblemHttpResult>> UpdateSlackChannel(
        UpdateSlackChannelRequest request,
        ScreenHireDbContext db,
        ITenantContext tenant,
        SlackAccess slack,
        SlackTokens tokens,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!slack.IsConfigured || await db.SlackInstallations.SingleOrDefaultAsync(cancellationToken) is not { } installation)
        {
            return TypedResults.NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.ChannelId) || request.ChannelId.Length > SlackInstallation.MaxIdLength)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("channelId", "Pick a channel."));
        }

        if (request.Events is null || request.Events.Any(e => !Enum.IsDefined(e)))
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("events", "Pick from the events listed."));
        }

        if (tokens.Unprotect(installation.OrganizationId, installation.ProtectedBotToken) is not { } token)
        {
            return await DisconnectedAsync(db, installation, time, cancellationToken);
        }

        SlackChannel? channel;
        try
        {
            channel = await slack.Client.GetChannelAsync(token, request.ChannelId, cancellationToken);
        }
        catch (SlackApiException e) when (e.Problem == SlackProblem.Disconnected)
        {
            return await DisconnectedAsync(db, installation, time, cancellationToken);
        }
        catch (SlackApiException)
        {
            return SlackDidNotAnswer();
        }

        var refusal = channel switch
        {
            null => "Slack has no such channel. Pick one from the list.",
            { IsArchived: true } => "That channel is archived. Pick another.",
            { IsPrivate: true, IsMember: false } => "ScreenHire is not in that private channel. Invite the app to it in Slack, or pick another.",
            _ => null,
        };
        if (refusal is not null)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("channelId", refusal));
        }

        installation.ChooseChannel(channel!.Id, channel.Name, request.Events, time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await StatusAsync(db, tenant, slack, cancellationToken));
    }

    private static async Task<Results<Ok<SlackTestResult>, NotFound, ValidationProblem, ProblemHttpResult>> SendSlackTestMessage(
        ScreenHireDbContext db, SlackAccess slack, SlackTokens tokens, TimeProvider time, CancellationToken cancellationToken)
    {
        if (!slack.IsConfigured || await db.SlackInstallations.SingleOrDefaultAsync(cancellationToken) is not { } installation)
        {
            return TypedResults.NotFound();
        }

        if (installation.ChannelId is not { } channelId)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("channelId", "Pick a channel first."));
        }

        var now = time.GetUtcNow();
        if (tokens.Unprotect(installation.OrganizationId, installation.ProtectedBotToken) is not { } token)
        {
            installation.RecordProblem(SlackProblem.Disconnected, now);
            await db.SaveChangesAsync(cancellationToken);
            return TypedResults.Ok(new SlackTestResult(false, SlackProblem.Disconnected));
        }

        try
        {
            await slack.Client.PostMessageAsync(token, channelId, SlackMessages.Test, cancellationToken);
        }
        catch (SlackApiException e) when (e.Problem is { } problem)
        {
            var recorded = SlackErrors.InChannel(problem);
            installation.RecordProblem(recorded, now);
            await db.SaveChangesAsync(cancellationToken);
            return TypedResults.Ok(new SlackTestResult(false, recorded));
        }
        catch (SlackApiException)
        {
            return SlackDidNotAnswer();
        }

        installation.ClearChannelProblem();
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(new SlackTestResult(true, null));
    }

    private static async Task<Results<NoContent, NotFound>> DisconnectSlack(
        ScreenHireDbContext db, SlackAccess slack, SlackTokens tokens, ILoggerFactory loggers, CancellationToken cancellationToken)
    {
        if (!slack.IsConfigured || await db.SlackInstallations.SingleOrDefaultAsync(cancellationToken) is not { } installation)
        {
            return TypedResults.NotFound();
        }

        // Best effort: the workspace goes from ScreenHire either way, and a token Slack already dropped cannot be revoked.
        if (tokens.Unprotect(installation.OrganizationId, installation.ProtectedBotToken) is { } token)
        {
            try
            {
                await slack.Client.RevokeAsync(token, cancellationToken);
            }
            catch (SlackApiException e)
            {
                LogRevokeFailed(loggers.CreateLogger(typeof(SlackEndpoints)), installation.OrganizationId, e.Code);
            }
        }

        // The account links go with it (a cascading foreign key).
        db.SlackInstallations.Remove(installation);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<SlackAuthorization>, NotFound, ProblemHttpResult>> StartSlackLink(
        StartSlackLinkRequest request,
        HttpContext context,
        ScreenHireDbContext db,
        ITenantContext tenant,
        SlackAccess slack,
        SlackOAuthState states,
        IOptions<DomainOptions> domains,
        CancellationToken cancellationToken)
    {
        if (NoOrganization(tenant) is { } refused)
        {
            return refused;
        }

        if (!slack.IsConfigured || await db.SlackInstallations.AsNoTracking().SingleOrDefaultAsync(cancellationToken) is not { } installation)
        {
            return TypedResults.NotFound();
        }

        var state = Issue(context, states, domains.Value, SlackOAuthPurpose.Link, tenant, ReturnUrls.Sanitize(request.ReturnUrl));
        return TypedResults.Ok(new SlackAuthorization(
            slack.Client.LinkUrl(state, domains.Value.AppUrl(CallbackPath), installation.TeamId).AbsoluteUri));
    }

    private static async Task<Results<Ok<SlackAccount>, NotFound, ProblemHttpResult>> UpdateSlackAccount(
        UpdateSlackAccountRequest request, ScreenHireDbContext db, ITenantContext tenant, SlackAccess slack, CancellationToken cancellationToken)
    {
        if (NoOrganization(tenant) is { } refused)
        {
            return refused;
        }

        if (!slack.IsConfigured || await db.SlackAccountLinks.SingleOrDefaultAsync(l => l.UserId == tenant.UserId, cancellationToken) is not { } link)
        {
            return TypedResults.NotFound();
        }

        link.Mentions = request.Mentions;
        link.Tasks = request.Tasks;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(link.ToAccount());
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> UnlinkSlackAccount(
        ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken)
    {
        if (NoOrganization(tenant) is { } refused)
        {
            return refused;
        }

        if (await db.SlackAccountLinks.SingleOrDefaultAsync(l => l.UserId == tenant.UserId, cancellationToken) is { } link)
        {
            db.SlackAccountLinks.Remove(link);
            await db.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.NoContent();
    }

    /// <summary>
    /// Where Slack sends the browser back, for an install and for a link alike. Every outcome is a redirect to the
    /// dashboard with <c>?slack=</c> (an install) or <c>?slack-account=</c> (a link): <c>connected</c>, <c>linked</c>,
    /// <c>denied</c> (cancelled on Slack's page), <c>expired</c>, <c>wrong-workspace</c> or <c>failed</c>.
    /// </summary>
    private static async Task<RedirectHttpResult> CompleteSlackOAuth(
        string? code,
        string? state,
        string? error,
        HttpContext context,
        ScreenHireDbContext db,
        ITenantContext tenant,
        SlackAccess slack,
        SlackOAuthState states,
        SlackTokens tokens,
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
            return TypedResults.Redirect(app.AppUrl($"/sign-in?returnUrl={Uri.EscapeDataString(SettingsPath)}").AbsoluteUri);
        }

        var round = states.Read(state);
        if (round is null)
        {
            return Outcome(app, SettingsPath, "expired");
        }

        if (nonce != round.Nonce || round.UserId != tenant.UserId || round.OrganizationId != tenant.OrganizationId || !slack.IsConfigured)
        {
            return Outcome(app, round, "failed");
        }

        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
        {
            return Outcome(app, round, error == "access_denied" ? "denied" : "failed");
        }

        var logger = loggers.CreateLogger(typeof(SlackEndpoints));
        var now = time.GetUtcNow();
        var redirectUri = app.AppUrl(CallbackPath);
        try
        {
            return round.Purpose == SlackOAuthPurpose.Install
                ? await InstallAsync(db, tenant, slack, tokens, code, redirectUri, round, app, now, cancellationToken)
                : await LinkAsync(db, tenant, slack, code, redirectUri, round, app, now, cancellationToken);
        }
        catch (SlackApiException e)
        {
            LogOAuthFailed(logger, round.Purpose, round.OrganizationId, e.Code);
            return Outcome(app, round, "failed");
        }
        catch (DbUpdateException)
        {
            // Two round trips finished at once and the other one saved first (one workspace, one link per person).
            LogOAuthFailed(logger, round.Purpose, round.OrganizationId, "concurrent_save");
            return Outcome(app, round, "failed");
        }
    }

    private static async Task<RedirectHttpResult> InstallAsync(
        ScreenHireDbContext db, ITenantContext tenant, SlackAccess slack, SlackTokens tokens, string code, Uri redirectUri,
        SlackOAuthStateData round, DomainOptions app, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // The permission is checked again: the role may have changed while the person was on Slack's page.
        var access = await RoleAccess.ForMemberAsync(db, round.OrganizationId, round.UserId, cancellationToken);
        if (access is null || !access.Permissions.Contains(Permission.ManageIntegrations) || !tenant.SeesAllJobs)
        {
            return Outcome(app, round, "failed");
        }

        var install = await slack.Client.CompleteInstallAsync(code, redirectUri, cancellationToken);
        var token = tokens.Protect(round.OrganizationId, install.BotToken);
        if (await db.SlackInstallations.SingleOrDefaultAsync(cancellationToken) is { } installation)
        {
            if (installation.Reconnect(install.TeamId, install.TeamName, install.BotUserId, token, round.UserId, now))
            {
                // Linked accounts belong to the old workspace.
                db.SlackAccountLinks.RemoveRange(await db.SlackAccountLinks.ToListAsync(cancellationToken));
            }
        }
        else
        {
            db.SlackInstallations.Add(SlackInstallation.Connect(
                round.OrganizationId, install.TeamId, install.TeamName, install.BotUserId, token, round.UserId, now));
        }

        await db.SaveChangesAsync(cancellationToken);
        return Outcome(app, round, "connected");
    }

    private static async Task<RedirectHttpResult> LinkAsync(
        ScreenHireDbContext db, ITenantContext tenant, SlackAccess slack, string code, Uri redirectUri,
        SlackOAuthStateData round, DomainOptions app, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await db.SlackInstallations.AsNoTracking().SingleOrDefaultAsync(cancellationToken) is not { } installation)
        {
            return Outcome(app, round, "failed");
        }

        var identity = await slack.Client.CompleteLinkAsync(code, redirectUri, cancellationToken);
        if (!string.Equals(identity.TeamId, installation.TeamId, StringComparison.Ordinal))
        {
            return Outcome(app, round, "wrong-workspace");
        }

        var name = identity.Name.Length > SlackAccountLink.MaxNameLength ? identity.Name[..SlackAccountLink.MaxNameLength] : identity.Name;
        var links = await db.SlackAccountLinks
            .Where(l => l.UserId == round.UserId || l.SlackUserId == identity.UserId)
            .ToListAsync(cancellationToken);
        // Whoever signed in to Slack owns that account, so a link from another member to it was stale.
        db.SlackAccountLinks.RemoveRange(links.Where(l => l.UserId != round.UserId));
        if (links.SingleOrDefault(l => l.UserId == round.UserId) is { } link)
        {
            link.Relink(identity.UserId, name, now);
        }
        else
        {
            db.SlackAccountLinks.Add(SlackAccountLink.Create(round.OrganizationId, installation.Id, tenant.UserId!.Value, identity.UserId, name, now));
        }

        await db.SaveChangesAsync(cancellationToken);
        return Outcome(app, round, "linked");
    }

    // The sandbox's consent page: none. It sends the browser straight back to our own callback with a code, as Slack
    // would after someone clicked Allow. Only to our callback, so it is never an open redirect.
    private static Results<RedirectHttpResult, BadRequest> AuthorizeInSandbox(
        string? kind, string? state, [FromQuery(Name = "redirect_uri")] string? redirectUri, ITenantContext tenant, IOptions<DomainOptions> domains)
    {
        var callback = domains.Value.AppUrl(CallbackPath);
        if (redirectUri != callback.ToString() || state is null || tenant.UserId is not { } userId)
        {
            return TypedResults.BadRequest();
        }

        var code = kind == "link" ? $"{SandboxSlackClient.LinkCodePrefix}{userId:N}" : SandboxSlackClient.InstallCode;
        return TypedResults.Redirect($"{callback}?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(state)}");
    }

    private static async Task<Ok<List<SlackSandboxMessageEntry>>> ListSandboxMessages(ScreenHireDbContext db, CancellationToken cancellationToken) =>
        TypedResults.Ok(await db.SlackSandboxMessages.AsNoTracking()
            .OrderByDescending(m => m.PostedAt).ThenByDescending(m => m.Id).Take(200)
            .Select(m => new SlackSandboxMessageEntry(m.Conversation, m.Text, m.PostedAt))
            .ToListAsync(cancellationToken));

    private static async Task<SlackStatus> StatusAsync(ScreenHireDbContext db, ITenantContext tenant, SlackAccess slack, CancellationToken cancellationToken)
    {
        if (!slack.IsConfigured)
        {
            return new SlackStatus(false, false, null, null, null);
        }

        var sandbox = slack.Mode == SlackMode.Sandbox;
        var installation = await db.SlackInstallations.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (installation is null)
        {
            return new SlackStatus(true, sandbox, null, null, null);
        }

        var link = tenant.UserId is { } userId
            ? await db.SlackAccountLinks.AsNoTracking().SingleOrDefaultAsync(l => l.UserId == userId, cancellationToken)
            : null;
        var manages = (await RoleAccess.PermissionsOfAsync(db, tenant, cancellationToken)).Contains(Permission.ManageIntegrations) && tenant.SeesAllJobs;
        SlackSettings? settings = null;
        if (manages)
        {
            var connectedBy = installation.ConnectedByUserId is { } by
                ? await db.Users.AsNoTracking().Where(u => u.Id == by).Select(u => u.DisplayName ?? u.Email).SingleOrDefaultAsync(cancellationToken)
                : null;
            settings = new SlackSettings(
                installation is { ChannelId: { } id, ChannelName: { } name } ? new SlackChannelChoice(id, name) : null,
                installation.Events,
                installation.ConnectedAt,
                connectedBy,
                installation.Problem,
                installation.ProblemAt);
        }

        return new SlackStatus(true, sandbox, new SlackWorkspace(installation.TeamName, installation.IsDisconnected), link?.ToAccount(), settings);
    }

    private static string Issue(
        HttpContext context, SlackOAuthState states, DomainOptions app, SlackOAuthPurpose purpose, ITenantContext tenant, string returnUrl)
    {
        var (state, nonce) = states.Issue(purpose, tenant.OrganizationId!.Value, tenant.UserId!.Value, returnUrl);
        context.Response.Cookies.Append(StateCookie, nonce, StateCookieOptions(app));
        return state;
    }

    // Lax, so the cookie arrives with Slack's top-level redirect back; only the callback reads it.
    private static CookieOptions StateCookieOptions(DomainOptions app) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Secure = app.Scheme == "https",
        Path = CallbackPath,
        MaxAge = SlackOAuthState.Lifetime,
    };

    // An install's outcome is ?slack=, a link's ?slack-account=: a link can start from any page, the Slack page included,
    // and each outcome belongs to the control that started it.
    private static RedirectHttpResult Outcome(DomainOptions app, SlackOAuthStateData round, string outcome) =>
        Outcome(app, round.ReturnUrl, outcome, round.Purpose == SlackOAuthPurpose.Link ? "slack-account" : "slack");

    private static RedirectHttpResult Outcome(DomainOptions app, string returnUrl, string outcome, string parameter = "slack")
    {
        var path = ReturnUrls.Sanitize(returnUrl);
        var separator = path.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return TypedResults.Redirect(app.AppUrl($"{path}{separator}{parameter}={outcome}").AbsoluteUri);
    }

    private static ProblemHttpResult? NoOrganization(ITenantContext tenant) =>
        tenant is { OrganizationId: not null, UserId: not null }
            ? null
            : TypedResults.Problem(title: "No organization", detail: "Create or join an organization first.", statusCode: StatusCodes.Status403Forbidden);

    private static async Task<ProblemHttpResult> DisconnectedAsync(
        ScreenHireDbContext db, SlackInstallation installation, TimeProvider time, CancellationToken cancellationToken)
    {
        installation.RecordProblem(SlackProblem.Disconnected, time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Problem(
            title: "Slack disconnected",
            detail: "Slack no longer accepts ScreenHire's connection. Reconnect Slack to keep posting.",
            statusCode: StatusCodes.Status409Conflict);
    }

    private static ProblemHttpResult SlackDidNotAnswer() =>
        TypedResults.Problem(title: "Slack did not answer", detail: "Slack did not answer in time. Try again in a minute.", statusCode: StatusCodes.Status502BadGateway);

    private static SlackAccount ToAccount(this SlackAccountLink link) => new(link.SlackName, link.LinkedAt, link.Mentions, link.Tasks, link.Problem);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Revoking organization {OrganizationId}'s Slack token failed: {Code}")]
    private static partial void LogRevokeFailed(ILogger logger, Guid organizationId, string code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Slack {Purpose} for organization {OrganizationId} failed: {Code}")]
    private static partial void LogOAuthFailed(ILogger logger, SlackOAuthPurpose purpose, Guid organizationId, string code);
}
