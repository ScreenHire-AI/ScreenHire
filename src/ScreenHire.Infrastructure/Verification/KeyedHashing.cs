using System.Security.Cryptography;
using System.Text;
using ScreenHire.Domain.Verification;

namespace ScreenHire.Infrastructure.Verification;

/// <summary>
/// Keyed hashes for identifiers that must be comparable but never readable: the per-organization device hash and the
/// Signal Network's identifier hashes (ADR-0005). HMAC-SHA256, lower-case hex.
/// </summary>
public static class KeyedHashing
{
    /// <summary>The smallest key accepted, in bytes.</summary>
    public const int MinimumKeyBytes = 32;

    /// <summary>
    /// A device fingerprint hashed with a salt per organization: the same device gives the same hash within one
    /// employer and unrelated hashes across employers, so device matches stay inside the organization (CLAUDE.md
    /// non-negotiable 6). Null when there is no fingerprint.
    /// </summary>
    public static string? DeviceHash(byte[] key, Guid organizationId, string? fingerprint)
    {
        ArgumentNullException.ThrowIfNull(key);
        return string.IsNullOrWhiteSpace(fingerprint) ? null : Hmac(key, $"device|{organizationId:N}|{fingerprint.Trim()}");
    }

    /// <summary>
    /// The Signal Network hash of an identifier, normalised first (email lower-cased with its plus tag removed, phone in
    /// E.164). Null for an identifier that does not normalise.
    /// </summary>
    public static string? NetworkHash(byte[] key, SignalNetworkIdentifierKind kind, string? identifier)
    {
        ArgumentNullException.ThrowIfNull(key);
        var normalized = kind switch
        {
            SignalNetworkIdentifierKind.Email => Identifiers.NormalizeEmail(identifier),
            SignalNetworkIdentifierKind.Phone => PhoneNumbersCheck.Inspect(identifier, null).E164,
            _ => string.IsNullOrWhiteSpace(identifier) ? null : identifier.Trim().ToLowerInvariant(),
        };
        return normalized is null ? null : Hmac(key, $"{kind}|{normalized}");
    }

    /// <summary>A pseudonym for a contributing organization, so the network can count organizations without naming them.</summary>
    public static string ContributorHash(byte[] key, Guid organizationId)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Hmac(key, $"organization|{organizationId:N}");
    }

    /// <summary>A key from configuration as bytes (UTF-8), or null when it is missing or shorter than <see cref="MinimumKeyBytes"/>.</summary>
    public static byte[]? ParseKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var bytes = Encoding.UTF8.GetBytes(key.Trim());
        return bytes.Length >= MinimumKeyBytes ? bytes : null;
    }

    private static string Hmac(byte[] key, string value) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(value)));
}
