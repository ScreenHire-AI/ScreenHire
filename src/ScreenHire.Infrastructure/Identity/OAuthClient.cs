namespace ScreenHire.Infrastructure.Identity;

/// <summary>
/// An OAuth client that registered itself (RFC 7591, ADR-0027): an AI assistant such as a claude.ai custom connector.
/// It belongs to no organization, because it registers before anyone signs in, so the table has no row-level security
/// and holds nothing but what the client sent: a name and the redirect URIs it may be sent back to. Public clients only,
/// so there is no client secret.
/// </summary>
public sealed class OAuthClient
{
    /// <summary>The longest client name kept; a longer one is cut.</summary>
    public const int MaxNameLength = 100;

    /// <summary>The client_id the client is given, in its hex form.</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Name { get; init; }

    /// <summary>The URIs the client may be redirected to. A request must name one of them exactly.</summary>
    public required List<string> RedirectUris { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>When a person last approved this client, so a registration nobody used can be purged.</summary>
    public DateTimeOffset? LastUsedAt { get; set; }
}
