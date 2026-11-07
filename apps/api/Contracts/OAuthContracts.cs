using ScreenHire.Domain.Organizations;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// An assistant's request to connect, as the consent page shows it: who is asking, where the person goes afterwards, which
/// organization the connection would belong to, and what it may do. <c>redirectHost</c> is the address's host (and port, for
/// a local one); <c>redirectIsLoopback</c> is true for an address on this person's own computer.
/// </summary>
public sealed record OAuthAuthorizationRequestInfo(
    string ClientName,
    string RedirectHost,
    bool RedirectIsLoopback,
    string OrganizationName,
    IReadOnlyList<OAuthScopeOffer> Scopes);

/// <summary>
/// One thing the assistant asked to be allowed. <c>grantable</c> is false when the signed-in person does not hold it
/// themselves; <c>reason</c> says why. Read is always offered and always granted.
/// </summary>
public sealed record OAuthScopeOffer(ApiKeyScope Scope, bool Grantable, string? Reason);

/// <summary>
/// The person's decision on an assistant's request: the OAuth parameters the assistant sent, copied as they came, plus
/// <c>approve</c> and the scopes the person chose. <c>grantedScopes</c> is cut to what the assistant asked for and the person
/// holds, and always includes Read.
/// </summary>
public sealed record OAuthAuthorizeRequest(
    string ClientId,
    string RedirectUri,
    string ResponseType,
    string? Scope,
    string? Resource,
    string CodeChallenge,
    string CodeChallengeMethod,
    string? State,
    bool Approve,
    IReadOnlyList<ApiKeyScope> GrantedScopes);

/// <summary>Where to send the person next: the assistant's redirect address, carrying the code or the refusal.</summary>
public sealed record OAuthAuthorizeResult(string RedirectUrl);

/// <summary>
/// An assistant a person connected to this organization. It acts with <c>scopes</c> and shows in activity under its name.
/// <c>lastUsedAt</c> is accurate to about a minute; <c>revokedAt</c> is set once a connection is revoked, and the row stays.
/// </summary>
public sealed record OAuthConnectionSummary(
    Guid Id,
    string ClientName,
    Guid? ConnectedByUserId,
    string? ConnectedByName,
    IReadOnlyList<ApiKeyScope> Scopes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt);
