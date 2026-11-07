namespace ScreenHire.Domain.Organizations;

/// <summary>
/// An authorization code the consent page issued to an OAuth client (ADR-0027): single use, short-lived, bound to the
/// redirect URI and the PKCE challenge of the request that asked for it. Only the SHA-256 hash of the code is stored.
/// </summary>
public sealed class OAuthCode : ITenantOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    /// <summary>The connection (an <see cref="ApiKey"/> row with an OAuth client) this code will open.</summary>
    public required Guid ApiKeyId { get; init; }

    /// <summary>Lower-case hex SHA-256 of the code.</summary>
    public required string CodeHash { get; init; }

    /// <summary>The redirect URI the request named; the token request must name the same one.</summary>
    public required string RedirectUri { get; init; }

    /// <summary>The PKCE S256 challenge: base64url of the SHA-256 of the verifier the client will present.</summary>
    public required string CodeChallenge { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>When the code was exchanged. A code works once.</summary>
    public DateTimeOffset? UsedAt { get; set; }
}

/// <summary>What an <see cref="OAuthToken"/> is for.</summary>
public enum OAuthTokenKind
{
    /// <summary>Sent on every MCP request. Short-lived.</summary>
    Access,

    /// <summary>Exchanged for a new pair of tokens. Single use: each exchange rotates it.</summary>
    Refresh,
}

/// <summary>
/// An access or refresh token of an OAuth connection (ADR-0027). Only the SHA-256 hash is stored. Every token belongs to
/// one connection, which is also its family: a refresh token presented twice means it leaked, so every token of the
/// connection is revoked.
/// </summary>
public sealed class OAuthToken : ITenantOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public required Guid ApiKeyId { get; init; }

    public required OAuthTokenKind Kind { get; init; }

    /// <summary>Lower-case hex SHA-256 of the token.</summary>
    public required string TokenHash { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>When a refresh token was exchanged. Null for an access token and for a refresh token not yet used.</summary>
    public DateTimeOffset? UsedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
}
