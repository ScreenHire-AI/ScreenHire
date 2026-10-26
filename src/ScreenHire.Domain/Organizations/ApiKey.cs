namespace ScreenHire.Domain.Organizations;

/// <summary>
/// An organization's key for the public API (ADR-0011). Only the SHA-256 hash of the key is stored; the key itself is
/// shown once, when it is created. <see cref="Prefix"/> is the key's first characters, which are not secret, so people
/// can tell their keys apart.
/// </summary>
public sealed class ApiKey : ITenantOwned
{
    /// <summary>The longest name a key may have.</summary>
    public const int MaxNameLength = 100;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public required string Name { get; init; }

    /// <summary>The start of the key, such as <c>shk_1a2b3c4d</c>.</summary>
    public required string Prefix { get; init; }

    /// <summary>Lower-case hex SHA-256 of the whole key.</summary>
    public required string SecretHash { get; init; }

    public required List<ApiKeyScope> Scopes { get; init; }

    /// <summary>Requests per minute this key may send, or null for the deployment's default.</summary>
    public int? PermitsPerMinute { get; init; }

    /// <summary>The person who created the key, or null once their account is deleted.</summary>
    public Guid? CreatedByUserId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the key last authenticated a request, to within a minute.</summary>
    public DateTimeOffset? LastUsedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>Whether the key carries <paramref name="scope"/>. <see cref="ApiKeyScope.Write"/> includes reading.</summary>
    public bool Allows(ApiKeyScope scope) => Allows(Scopes, scope);

    public static bool Allows(IEnumerable<ApiKeyScope> scopes, ApiKeyScope scope) =>
        scopes.Contains(scope) || (scope == ApiKeyScope.Read && scopes.Contains(ApiKeyScope.Write));

    /// <summary>Revokes the key. A key that is already revoked keeps its first revocation time.</summary>
    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}

/// <summary>What an API key may do on the hiring API.</summary>
public enum ApiKeyScope
{
    /// <summary>Read jobs, applications, candidates and templates.</summary>
    Read,

    /// <summary>Change them too. Includes <see cref="Read"/>.</summary>
    Write,
}
