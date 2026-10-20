namespace ScreenHire.Domain.Integrity;

/// <summary>
/// One sighting of a hard identifier in the cross-employer Signal Network (ADR-0005, integrity-engine.md §6): a keyed
/// HMAC of the normalised identifier, never the identifier, name, content, outcome or employer. Not tenant-owned on
/// purpose; the application role has no access to the table at all, and nothing writes or reads it while the network's
/// feature flag is off, which it is in every environment until legal review and DPIA sign-off.
/// </summary>
public sealed class SignalNetworkEntry
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public SignalNetworkIdentifierKind Kind { get; init; }

    /// <summary>Lower-case hex HMAC-SHA256 of the normalised identifier under <see cref="KeyId"/>.</summary>
    public required string IdentifierHash { get; init; }

    /// <summary>
    /// A keyed hash of the contributing organization, so the network can count distinct organizations (shown only when
    /// there are at least three) without storing which employer contributed.
    /// </summary>
    public required string ContributorHash { get; init; }

    /// <summary>Which network key made the hashes. Rotating the key starts new hashes; old ones expire with retention.</summary>
    public required string KeyId { get; init; }

    public DateTimeOffset SeenAt { get; init; }

    /// <summary>90 days after <see cref="SeenAt"/> (ADR-0005), when the retention job deletes the row.</summary>
    public DateTimeOffset ExpiresAt { get; init; }

    public static readonly TimeSpan Retention = TimeSpan.FromDays(90);
}

public enum SignalNetworkIdentifierKind
{
    Email,
    Phone,
    Device,
    ResumeFile,
}
