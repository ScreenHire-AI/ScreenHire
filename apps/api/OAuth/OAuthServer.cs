using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Configuration;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Identity;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.OAuth;

/// <summary>What a client sent to the authorization endpoint, as the consent page forwards it.</summary>
internal sealed record AuthorizationParameters(
    string? ClientId,
    string? RedirectUri,
    string? ResponseType,
    string? Scope,
    string? Resource,
    string? CodeChallenge,
    string? CodeChallengeMethod,
    string? State);

/// <summary>An authorization request that passed every check the server can make before a person decides.</summary>
internal sealed record ValidatedAuthorization(
    OAuthClient Client, string RedirectUri, IReadOnlyList<ApiKeyScope> Requested, string CodeChallenge, string? State);

/// <summary>The body of an error response from the token, registration and revocation endpoints (RFC 6749 section 5.2).</summary>
internal sealed record OAuthError(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("error_description")] string? ErrorDescription = null);

/// <summary>A successful token response (RFC 6749 section 5.1).</summary>
internal sealed record OAuthTokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("scope")] string Scope);

/// <summary>
/// The authorization server's rules (docs/architecture/oauth-server.md, ADR-0028): checking an authorization request,
/// recording a person's decision as a connection and a code, and exchanging a code or a refresh token for tokens. One
/// scoped service so the consent routes and the token endpoint apply the same checks. The consent routes run for a
/// signed-in person, whose organization is already the tenant; the token endpoint sets the tenant from the code or token
/// it is given, then looks it up under row-level security, as the API key handler does.
/// </summary>
internal sealed class OAuthServer(
    ScreenHireDbContext db,
    TenantContext tenant,
    TimeProvider time,
    IOptions<OAuthOptions> options,
    IOptions<DomainOptions> domains)
{
    private const int MaxStateLength = 1024;

    // ---- The consent page ----

    /// <summary>Checks an authorization request. The problem is plain text for the consent page to show; nothing redirects to an unchecked address.</summary>
    public async Task<(ValidatedAuthorization? Valid, string? Problem)> CheckAsync(AuthorizationParameters request, CancellationToken cancellationToken)
    {
        if (!TryParseClientId(request.ClientId, out var clientId)
            || await db.OAuthClients.AsNoTracking().SingleOrDefaultAsync(c => c.Id == clientId, cancellationToken) is not { } client)
        {
            return (null, "This assistant is not registered with ScreenHire. Start the connection again from the assistant.");
        }

        if (request.RedirectUri is not { Length: > 0 } redirect || !client.RedirectUris.Any(registered => OAuthRedirects.Matches(registered, redirect)))
        {
            return (null, "The address this assistant wants to send you back to is not one it registered. Start the connection again from the assistant.");
        }

        if (request.ResponseType != "code")
        {
            return (null, "This assistant asked for something other than an authorization code, which ScreenHire does not offer.");
        }

        if (request.CodeChallengeMethod != Pkce.Method || !Pkce.IsChallenge(request.CodeChallenge))
        {
            return (null, "This assistant did not send the PKCE (S256) challenge that ScreenHire requires. Update the assistant or start again.");
        }

        if (!string.IsNullOrEmpty(request.Resource) && request.Resource != OAuthMetadata.Resource(domains.Value))
        {
            return (null, "This assistant asked to connect to a server other than this ScreenHire's MCP server.");
        }

        if (request.State is { Length: > MaxStateLength })
        {
            return (null, "The request from this assistant is too long. Start the connection again.");
        }

        return (new ValidatedAuthorization(client, redirect, OAuthScopes.Parse(request.Scope), request.CodeChallenge!, request.State), null);
    }

    /// <summary>
    /// What the current person may give a connection, by the rule that creates an API key: Write needs every hiring write
    /// their role has, and Verification read needs the Owner-only permission (ADR-0011, ADR-0021). The scopes keys and
    /// connections can hold are the same, so a person never grants more than they hold.
    /// </summary>
    public async Task<(bool Write, bool VerificationRead)> GrantableAsync(CancellationToken cancellationToken)
    {
        var access = await RoleAccess.ForMemberAsync(db, tenant.OrganizationId!.Value, tenant.UserId!.Value, cancellationToken);
        return access is null
            ? (false, false)
            : (RolePermissions.HiringWrites.IsSubsetOf(access.Permissions), access.Permissions.Contains(Permission.GrantVerificationAccess));
    }

    /// <summary>
    /// Records an approval: a connection (a key row nobody can sign in with as a key) holding the chosen scopes, a code for
    /// the client to exchange, and the redirect that carries it. Earlier connections of the same person to the same assistant
    /// are revoked, because an assistant such as claude.ai registers a new client on every connection and would otherwise
    /// leave a live connection behind each time.
    /// </summary>
    public async Task<string> ApproveAsync(
        ValidatedAuthorization request, IReadOnlyList<ApiKeyScope> chosen, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var organizationId = tenant.OrganizationId!.Value;
        var userId = tenant.UserId!.Value;
        List<ApiKeyScope> scopes = [.. chosen.Append(ApiKeyScope.Read).Distinct().Order()];

        var earlier = await (
            from key in db.ApiKeys
            join client in db.OAuthClients on key.OAuthClientId equals client.Id
            where key.RevokedAt == null && key.CreatedByUserId == userId && client.Name == request.Client.Name
            select new { Key = key, client.RedirectUris })
            .ToListAsync(cancellationToken);
        var redirects = request.Client.RedirectUris.Order().ToList();
        foreach (var match in earlier.Where(e => e.RedirectUris.Order().SequenceEqual(redirects)))
        {
            match.Key.Revoke(now);
        }

        // The key text is dropped: only its hash is stored, so this row can never sign in as an shk_ key.
        var (_, prefix, secretHash) = ApiKeyTokens.Create(organizationId);
        var connection = new ApiKey
        {
            OrganizationId = organizationId,
            Name = request.Client.Name,
            Prefix = prefix,
            SecretHash = secretHash,
            Scopes = scopes,
            CreatedByUserId = userId,
            CreatedAt = now,
            OAuthClientId = request.Client.Id,
        };
        db.ApiKeys.Add(connection);

        var (code, codeHash) = OAuthTokens.Create(OAuthTokens.CodeMarker, organizationId);
        db.OAuthCodes.Add(new OAuthCode
        {
            OrganizationId = organizationId,
            ApiKeyId = connection.Id,
            CodeHash = codeHash,
            RedirectUri = request.RedirectUri,
            CodeChallenge = request.CodeChallenge,
            CreatedAt = now,
            ExpiresAt = now + options.Value.CodeLifetime,
        });

        // In the same save as the connection: the purge deletes a registration nobody used, and a connection that still
        // names it would stop the purge at the foreign key every day.
        db.OAuthClients.Attach(request.Client);
        request.Client.LastUsedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        return QueryHelpers.AddQueryString(request.RedirectUri, WithState(request.State, new() { ["code"] = code }));
    }

    /// <summary>The redirect that tells the assistant the person said no (RFC 6749 section 4.1.2.1).</summary>
    public string Deny(ValidatedAuthorization request) =>
        QueryHelpers.AddQueryString(request.RedirectUri, WithState(request.State, new() { ["error"] = "access_denied" }));

    private Dictionary<string, string?> WithState(string? state, Dictionary<string, string?> parameters)
    {
        if (!string.IsNullOrEmpty(state))
        {
            parameters["state"] = state;
        }

        // RFC 9207: tells the client which server answered, so a mixed-up server cannot pass it a code.
        parameters["iss"] = OAuthMetadata.Issuer(domains.Value);
        return parameters;
    }

    // ---- The token endpoint ----

    /// <summary>Exchanges an authorization code for tokens (RFC 6749 section 4.1.3, PKCE RFC 7636).</summary>
    public async Task<IResult> ExchangeCodeAsync(IFormCollection form, CancellationToken cancellationToken)
    {
        var code = form["code"].ToString();
        if (!OAuthTokens.TryReadOrganization(OAuthTokens.CodeMarker, code, out var organizationId))
        {
            return Error("invalid_grant", "The code is not valid.");
        }

        tenant.SetOrganization(organizationId.Value);
        var now = time.GetUtcNow();
        var hash = OAuthTokens.Hash(code);

        var row = await (
            from c in db.OAuthCodes.AsNoTracking()
            join k in db.ApiKeys.AsNoTracking() on c.ApiKeyId equals k.Id
            where c.CodeHash == hash
            select new { c.RedirectUri, c.CodeChallenge, KeyId = k.Id, k.OAuthClientId, k.RevokedAt, k.Scopes })
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return Error("invalid_grant", "The code is expired or was already used.");
        }

        // The proof comes before the code is spent, so someone who saw a code but cannot finish the exchange can neither
        // use it up nor end the connection.
        if (!TryParseClientId(form["client_id"], out var clientId) || row.OAuthClientId != clientId)
        {
            return Error("invalid_grant", "The code was issued to a different client.");
        }

        if (form["redirect_uri"] != row.RedirectUri)
        {
            return Error("invalid_grant", "The redirect_uri does not match the authorization request.");
        }

        if (!Pkce.Verify(form["code_verifier"], row.CodeChallenge))
        {
            return Error("invalid_grant", "The code_verifier does not match the code_challenge.");
        }

        if (ResourceProblem(form) is { } resource)
        {
            return resource;
        }

        // One conditional UPDATE, so two simultaneous exchanges cannot both succeed.
        var consumed = await db.OAuthCodes
            .Where(c => c.CodeHash == hash && c.UsedAt == null && c.ExpiresAt > now)
            .ExecuteUpdateAsync(set => set.SetProperty(c => c.UsedAt, now), cancellationToken);
        if (consumed == 0)
        {
            // A code used twice means someone else may hold it: end the connection's tokens (RFC 6749 section 4.1.2).
            if (await db.OAuthCodes.AsNoTracking().AnyAsync(c => c.CodeHash == hash && c.UsedAt != null, cancellationToken))
            {
                await RevokeTokensAsync(row.KeyId, now, cancellationToken);
            }

            return Error("invalid_grant", "The code is expired or was already used.");
        }

        if (row.RevokedAt is not null)
        {
            return Error("invalid_grant", "The connection was revoked.");
        }

        return await IssueAsync(organizationId.Value, row.KeyId, row.Scopes, now, cancellationToken);
    }

    /// <summary>Exchanges a refresh token for a new pair, rotating it (OAuth 2.1). A refresh token used twice ends the connection's tokens.</summary>
    public async Task<IResult> RefreshAsync(IFormCollection form, CancellationToken cancellationToken)
    {
        var refresh = form["refresh_token"].ToString();
        if (!OAuthTokens.TryReadOrganization(OAuthTokens.RefreshMarker, refresh, out var organizationId))
        {
            return Error("invalid_grant", "The refresh token is not valid.");
        }

        tenant.SetOrganization(organizationId.Value);
        var now = time.GetUtcNow();
        var hash = OAuthTokens.Hash(refresh);

        var row = await (
            from t in db.OAuthTokens.AsNoTracking()
            join k in db.ApiKeys.AsNoTracking() on t.ApiKeyId equals k.Id
            where t.TokenHash == hash && t.Kind == OAuthTokenKind.Refresh
            select new { KeyId = k.Id, k.OAuthClientId, k.RevokedAt, k.Scopes })
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return Error("invalid_grant", "The refresh token is expired, revoked or was already used.");
        }

        // As for a code: the checks come before the token is spent, so a refused request costs the client nothing.
        if (!TryParseClientId(form["client_id"], out var clientId) || row.OAuthClientId != clientId)
        {
            return Error("invalid_grant", "The refresh token was issued to a different client.");
        }

        if (ResourceProblem(form) is { } resource)
        {
            return resource;
        }

        var consumed = await db.OAuthTokens
            .Where(t => t.TokenHash == hash && t.Kind == OAuthTokenKind.Refresh && t.UsedAt == null && t.RevokedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.UsedAt, now), cancellationToken);
        if (consumed == 0)
        {
            if (await db.OAuthTokens.AsNoTracking().AnyAsync(t => t.TokenHash == hash && t.Kind == OAuthTokenKind.Refresh && t.UsedAt != null, cancellationToken))
            {
                await RevokeTokensAsync(row.KeyId, now, cancellationToken);
            }

            return Error("invalid_grant", "The refresh token is expired, revoked or was already used.");
        }

        if (row.RevokedAt is not null)
        {
            return Error("invalid_grant", "The connection was revoked.");
        }

        return await IssueAsync(organizationId.Value, row.KeyId, row.Scopes, now, cancellationToken);
    }

    /// <summary>
    /// Revokes a token (RFC 7009). Revoking a refresh token ends every token of its connection; an access token ends only
    /// itself. An unknown token is not an error, so the endpoint tells a caller nothing about which tokens exist. When the
    /// caller names a <paramref name="clientId"/> the token must be that client's: another client's token is left alone,
    /// and the answer is the same (RFC 7009 section 2.1). A public client has no secret to prove itself with, so a caller
    /// that names no client is judged by holding the token alone.
    /// </summary>
    public async Task RevokeAsync(string? token, string? clientId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var (marker, kind) = OAuthTokens.TryReadOrganization(OAuthTokens.RefreshMarker, token, out _)
            ? (OAuthTokens.RefreshMarker, OAuthTokenKind.Refresh)
            : (OAuthTokens.AccessMarker, OAuthTokenKind.Access);
        if (!OAuthTokens.TryReadOrganization(marker, token, out var organizationId))
        {
            return;
        }

        tenant.SetOrganization(organizationId.Value);
        var hash = OAuthTokens.Hash(token!);
        var owner = await (
            from t in db.OAuthTokens.AsNoTracking()
            join k in db.ApiKeys.AsNoTracking() on t.ApiKeyId equals k.Id
            where t.TokenHash == hash && t.Kind == kind
            select new { t.ApiKeyId, k.OAuthClientId })
            .SingleOrDefaultAsync(cancellationToken);
        if (owner is null || (!string.IsNullOrEmpty(clientId) && (!TryParseClientId(clientId, out var named) || named != owner.OAuthClientId)))
        {
            return;
        }

        if (kind == OAuthTokenKind.Refresh)
        {
            await RevokeTokensAsync(owner.ApiKeyId, now, cancellationToken);
        }
        else
        {
            await db.OAuthTokens.Where(t => t.TokenHash == hash && t.Kind == OAuthTokenKind.Access && t.RevokedAt == null)
                .ExecuteUpdateAsync(set => set.SetProperty(t => t.RevokedAt, now), cancellationToken);
        }
    }

    /// <summary>Ends every token of a connection that has not ended.</summary>
    public Task<int> RevokeTokensAsync(Guid keyId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.OAuthTokens.Where(t => t.ApiKeyId == keyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.RevokedAt, now), cancellationToken);

    private async Task<IResult> IssueAsync(
        Guid organizationId, Guid keyId, IReadOnlyList<ApiKeyScope> scopes, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var (access, accessHash) = OAuthTokens.Create(OAuthTokens.AccessMarker, organizationId);
        var (refresh, refreshHash) = OAuthTokens.Create(OAuthTokens.RefreshMarker, organizationId);
        db.OAuthTokens.Add(new OAuthToken
        {
            OrganizationId = organizationId,
            ApiKeyId = keyId,
            Kind = OAuthTokenKind.Access,
            TokenHash = accessHash,
            CreatedAt = now,
            ExpiresAt = now + options.Value.AccessTokenLifetime,
        });
        db.OAuthTokens.Add(new OAuthToken
        {
            OrganizationId = organizationId,
            ApiKeyId = keyId,
            Kind = OAuthTokenKind.Refresh,
            TokenHash = refreshHash,
            CreatedAt = now,
            ExpiresAt = now + options.Value.RefreshTokenLifetime,
        });
        await db.SaveChangesAsync(cancellationToken);

        return Results.Json(new OAuthTokenResponse(
            access, "Bearer", (int)options.Value.AccessTokenLifetime.TotalSeconds, refresh, OAuthScopes.Format(scopes)));
    }

    // A client may name the resource again at the token endpoint (RFC 8707); when it does it must be this server.
    private IResult? ResourceProblem(IFormCollection form) =>
        form["resource"].ToString() is { Length: > 0 } resource && resource != OAuthMetadata.Resource(domains.Value)
            ? Error("invalid_target", "The resource is not this server's MCP endpoint.")
            : null;

    public static IResult Error(string error, string? description = null, int status = StatusCodes.Status400BadRequest) =>
        Results.Json(new OAuthError(error, description), statusCode: status);

    public static bool TryParseClientId(string? value, out Guid clientId) => Guid.TryParse(value, out clientId);
}
