using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Configuration;
using ScreenHire.Infrastructure.Identity;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Api.OAuth;

/// <summary>
/// The protocol endpoints of the OAuth authorization server that signs AI assistants in to the MCP server
/// (docs/architecture/oauth-server.md, ADR-0028): the two discovery documents, dynamic client registration (RFC 7591),
/// the token endpoint and revocation. All are anonymous, because an assistant calls them before it holds a credential,
/// rate limited by client address, and open to any origin for browser-based clients such as the MCP Inspector. None is in
/// the OpenAPI document: the dashboard never calls them, and OAuth describes them itself. The consent routes, which need
/// a signed-in person, are in <see cref="OAuthConsentEndpoints"/>.
/// </summary>
internal static class OAuthEndpoints
{
    private const int MaxRegistrationBytes = 16 * 1024;
    private static readonly string[] SupportedGrantTypes = ["authorization_code", "refresh_token"];

    public static IEndpointRouteBuilder MapOAuthEndpoints(this IEndpointRouteBuilder app)
    {
        MapDocument(app, OAuthMetadata.ProtectedResourcePath, (DomainOptions domains) => OAuthMetadata.ProtectedResource(domains));
        MapDocument(app, "/.well-known/oauth-protected-resource", (DomainOptions domains) => OAuthMetadata.ProtectedResource(domains));
        MapDocument(app, OAuthMetadata.AuthorizationServerPath, (DomainOptions domains) => OAuthMetadata.AuthorizationServer(domains));
        MapDocument(app, OAuthMetadata.OpenIdConfigurationPath, (DomainOptions domains) => OAuthMetadata.AuthorizationServer(domains));

        app.MapPost(OAuthMetadata.RegisterPath, RegisterClient).WithName("RegisterOAuthClient")
            .AllowAnonymous().RequireCors(OAuthSetup.CorsPolicy).RequireRateLimiting(OAuthSetup.RegisterLimit).ExcludeFromDescription();
        app.MapPost(OAuthMetadata.TokenPath, Token).WithName("OAuthToken")
            .AllowAnonymous().RequireCors(OAuthSetup.CorsPolicy).RequireRateLimiting(OAuthSetup.TokenLimit).ExcludeFromDescription();
        app.MapPost(OAuthMetadata.RevokePath, Revoke).WithName("OAuthRevoke")
            .AllowAnonymous().RequireCors(OAuthSetup.CorsPolicy).RequireRateLimiting(OAuthSetup.TokenLimit).ExcludeFromDescription();
        return app;
    }

    private static void MapDocument<T>(IEndpointRouteBuilder app, string path, Func<DomainOptions, T> build) =>
        app.MapGet(path, (IOptions<DomainOptions> domains) => TypedResults.Json(build(domains.Value)))
            .AllowAnonymous().RequireCors(OAuthSetup.CorsPolicy).ExcludeFromDescription();

    // RFC 7591. Public clients only: no secret is issued, so PKCE protects every code.
    private static async Task<IResult> RegisterClient(
        HttpContext context, ScreenHireDbContext db, TimeProvider time, IOptions<OAuthOptions> options, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";

        // The server's own limit, because a chunked body has no Content-Length to check first.
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodyLimit)
        {
            bodyLimit.MaxRequestBodySize = MaxRegistrationBytes;
        }

        JsonElement body;
        try
        {
            using var document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: cancellationToken);
            body = document.RootElement.Clone();
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return OAuthServer.Error("invalid_client_metadata", "The registration is too large.");
        }
        catch (JsonException)
        {
            return OAuthServer.Error("invalid_client_metadata", "The body must be a JSON object.");
        }

        if (body.ValueKind != JsonValueKind.Object)
        {
            return OAuthServer.Error("invalid_client_metadata", "The body must be a JSON object.");
        }

        if (!TryStrings(body, "redirect_uris", out var redirects) || redirects is not { Count: > 0 })
        {
            return OAuthServer.Error("invalid_redirect_uri", "redirect_uris must list at least one address.");
        }

        if (redirects.Count > OAuthRedirects.MaxPerClient || redirects.Any(r => !OAuthRedirects.IsAllowed(r)))
        {
            return OAuthServer.Error("invalid_redirect_uri", "Each redirect URI must be an https address, or an http address on localhost.");
        }

        if (body.TryGetProperty("token_endpoint_auth_method", out var method) && method.ValueKind == JsonValueKind.String && method.GetString() != "none")
        {
            return OAuthServer.Error("invalid_client_metadata", "Only public clients are supported: token_endpoint_auth_method must be none.");
        }

        if (!TryStrings(body, "grant_types", out var grants) || (grants is not null && (!grants.Contains("authorization_code") || grants.Except(SupportedGrantTypes).Any())))
        {
            return OAuthServer.Error("invalid_client_metadata", "grant_types may hold authorization_code and refresh_token.");
        }

        if (!TryStrings(body, "response_types", out var responses) || (responses is not null && responses.Except(["code"]).Any()))
        {
            return OAuthServer.Error("invalid_client_metadata", "response_types may hold code only.");
        }

        // Open registration must not fill the table: past the cap, an assistant waits for the daily purge of unused registrations.
        if (await db.OAuthClients.CountAsync(c => c.LastUsedAt == null, cancellationToken) >= options.Value.MaxUnapprovedClients)
        {
            return OAuthServer.Error("temporarily_unavailable", "Too many registrations are waiting. Try again later.", StatusCodes.Status429TooManyRequests);
        }

        var name = body.TryGetProperty("client_name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
            ? CleanName(nameElement.GetString())
            : "";
        var now = time.GetUtcNow();
        var client = new OAuthClient
        {
            Name = name.Length > 0 ? name : "MCP client",
            RedirectUris = [.. redirects.Distinct()],
            CreatedAt = now,
        };
        db.OAuthClients.Add(client);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Json(
            new ClientRegistrationResponse(
                client.Id.ToString("N"), now.ToUnixTimeSeconds(), client.Name, client.RedirectUris, "none", SupportedGrantTypes, ["code"]),
            statusCode: StatusCodes.Status201Created);
    }

    // The token and revocation endpoints set the tenant from the organization named inside the code or token they are given.
    // They do it in a scope of their own, as the authentication handler does, so that text from outside never becomes the
    // tenant of the request itself, which a signed-in browser may also carry a session for.
    private static async Task<IResult> Token(HttpContext context, IServiceScopeFactory scopes, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var server = scope.ServiceProvider.GetRequiredService<OAuthServer>();
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
        if (!context.Request.HasFormContentType)
        {
            return OAuthServer.Error("invalid_request", "Send the request as application/x-www-form-urlencoded.");
        }

        var form = await context.Request.ReadFormAsync(cancellationToken);
        return form["grant_type"].ToString() switch
        {
            "authorization_code" => await server.ExchangeCodeAsync(form, cancellationToken),
            "refresh_token" => await server.RefreshAsync(form, cancellationToken),
            _ => OAuthServer.Error("unsupported_grant_type", "Use authorization_code or refresh_token."),
        };
    }

    private static async Task<IResult> Revoke(HttpContext context, IServiceScopeFactory scopes, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var server = scope.ServiceProvider.GetRequiredService<OAuthServer>();
        context.Response.Headers.CacheControl = "no-store";
        if (!context.Request.HasFormContentType)
        {
            return OAuthServer.Error("invalid_request", "Send the request as application/x-www-form-urlencoded.");
        }

        var form = await context.Request.ReadFormAsync(cancellationToken);
        await server.RevokeAsync(form["token"], form["client_id"], cancellationToken);
        return Results.Ok();
    }

    // An absent member is fine (null list); a member that is not an array of strings is not.
    private static bool TryStrings(JsonElement body, string name, out List<string>? values)
    {
        values = null;
        if (!body.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Array || element.EnumerateArray().Any(e => e.ValueKind != JsonValueKind.String))
        {
            return false;
        }

        values = [.. element.EnumerateArray().Select(e => e.GetString()!)];
        return true;
    }

    // The name appears on the consent page and in the activity of every change the assistant makes: plain text only, with
    // nothing invisible, because a right-to-left override or a zero-width space can make one name read as another.
    internal static string CleanName(string? name)
    {
        var text = new string([.. (name ?? "").Where(IsVisibleText)]).Trim();
        return text.Length > OAuthClient.MaxNameLength ? text[..OAuthClient.MaxNameLength].TrimEnd() : text;
    }

    private static bool IsVisibleText(char c) => char.GetUnicodeCategory(c) is not (
        UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator);
}

/// <summary>RFC 7591 section 3.2.1: what a client is told about its registration.</summary>
internal sealed record ClientRegistrationResponse(
    [property: JsonPropertyName("client_id")] string ClientId,
    [property: JsonPropertyName("client_id_issued_at")] long ClientIdIssuedAt,
    [property: JsonPropertyName("client_name")] string ClientName,
    [property: JsonPropertyName("redirect_uris")] IReadOnlyList<string> RedirectUris,
    [property: JsonPropertyName("token_endpoint_auth_method")] string TokenEndpointAuthMethod,
    [property: JsonPropertyName("grant_types")] IReadOnlyList<string> GrantTypes,
    [property: JsonPropertyName("response_types")] IReadOnlyList<string> ResponseTypes);
