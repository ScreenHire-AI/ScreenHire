namespace ScreenHire.Infrastructure.Identity;

/// <summary>
/// An emailed sign-in link. Only the SHA-256 hash of the token is stored, so a database leak cannot sign anyone in.
/// A token works once (<see cref="ConsumedAt"/>) and only until <see cref="ExpiresAt"/>.
/// </summary>
/// <remarks>
/// ASP.NET Core Identity's token providers are stateless (data-protected payloads that stay valid until they expire)
/// and need an existing user, so they cannot give single use or sign-up by email. Hence this table.
/// </remarks>
public sealed class MagicLinkToken
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>The address the link was sent to, trimmed and lower-cased.</summary>
    public required string Email { get; init; }

    /// <summary>Lower-case hex SHA-256 of the token in the link.</summary>
    public required string TokenHash { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public DateTimeOffset? ConsumedAt { get; set; }
}
