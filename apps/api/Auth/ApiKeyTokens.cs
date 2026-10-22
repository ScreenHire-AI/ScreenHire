using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ScreenHire.Api.Auth;

/// <summary>
/// The text of an API key: <c>shk_{prefix}_{organization}_{secret}</c>, all lower-case hex. The prefix (8 digits) tells
/// keys apart in a list, the organization id lets authentication set the tenant before it looks the key up, so the
/// lookup runs under row-level security like every other tenant query, and the secret is 256 random bits.
/// </summary>
/// <remarks>
/// Only <see cref="Hash"/> of the whole key is stored. A fast hash is enough: the secret is random and long, so there is
/// no dictionary to try, unlike a password.
/// </remarks>
internal static class ApiKeyTokens
{
    public const string Marker = "shk";

    private const int PrefixLength = 8;
    private const int OrganizationLength = 32;
    private const int SecretLength = 64;

    /// <summary>A new key for <paramref name="organizationId"/>, with its display prefix and its hash.</summary>
    public static (string Token, string Prefix, string Hash) Create(Guid organizationId)
    {
        var prefix = $"{Marker}_{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(PrefixLength / 2))}";
        var secret = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SecretLength / 2));
        var token = string.Create(CultureInfo.InvariantCulture, $"{prefix}_{organizationId:N}_{secret}");
        return (token, prefix, Hash(token));
    }

    /// <summary>The organization a well-formed key names. Says nothing about whether the key exists.</summary>
    public static bool TryReadOrganization(string token, [NotNullWhen(true)] out Guid? organizationId)
    {
        organizationId = null;
        var parts = token.Split('_');
        if (parts is not [Marker, var prefix, var organization, var secret]
            || prefix.Length != PrefixLength || !IsHex(prefix)
            || organization.Length != OrganizationLength || !IsHex(organization)
            || secret.Length != SecretLength || !IsHex(secret))
        {
            return false;
        }

        organizationId = Guid.ParseExact(organization, "N");
        return true;
    }

    /// <summary>Lower-case hex SHA-256 of the key, as stored.</summary>
    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static bool IsHex(string value) => value.All(char.IsAsciiHexDigitLower);
}
