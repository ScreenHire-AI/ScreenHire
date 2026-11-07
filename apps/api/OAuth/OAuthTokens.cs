using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ScreenHire.Api.Auth;

namespace ScreenHire.Api.OAuth;

// Why this is hand-written rather than OpenIddict (ADR-0028, .claude/rules/cli-features-over-custom-layers.md): an API key
// names its organization inside the token, so authentication sets the tenant first and the lookup runs under row-level
// security (CLAUDE.md non-negotiable 6). OpenIddict finds a reference token by its hash alone, with no organization to
// set before the query, so its tables would have to sit outside row-level security.

/// <summary>
/// The text of an authorization code or an OAuth token: <c>{marker}_{organization}_{secret}</c>, lower-case hex, with
/// <c>shc</c> for a code, <c>sho</c> for an access token and <c>shr</c> for a refresh token. Like an API key
/// (<see cref="ApiKeyTokens"/>), it names its organization so that the lookup runs under row-level security, and only the
/// hash of the whole string is stored. The markers differ from <c>shk</c>, so a token can never be read as a key.
/// </summary>
internal static class OAuthTokens
{
    public const string CodeMarker = "shc";
    public const string AccessMarker = "sho";
    public const string RefreshMarker = "shr";

    private const int OrganizationLength = 32;
    private const int SecretLength = 64;

    public static (string Token, string Hash) Create(string marker, Guid organizationId)
    {
        var secret = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SecretLength / 2));
        var token = string.Create(CultureInfo.InvariantCulture, $"{marker}_{organizationId:N}_{secret}");
        return (token, Hash(token));
    }

    /// <summary>The organization a well-formed token with <paramref name="marker"/> names. Says nothing about whether it exists.</summary>
    public static bool TryReadOrganization(string marker, string? token, [NotNullWhen(true)] out Guid? organizationId)
    {
        organizationId = null;
        if (token is null || token.Split('_') is not [var m, var organization, var secret]
            || m != marker
            || organization.Length != OrganizationLength || !IsHex(organization)
            || secret.Length != SecretLength || !IsHex(secret))
        {
            return false;
        }

        organizationId = Guid.ParseExact(organization, "N");
        return true;
    }

    /// <summary>Whether the text starts like an access token, which picks the handler that reads it.</summary>
    public static bool LooksLikeAccessToken(string? token) =>
        token is not null && token.StartsWith(AccessMarker + "_", StringComparison.Ordinal);

    public static string Hash(string token) => ApiKeyTokens.Hash(token);

    private static bool IsHex(string value) => value.All(char.IsAsciiHexDigitLower);
}

/// <summary>PKCE with the S256 method (RFC 7636), the only one offered: OAuth 2.1 drops <c>plain</c>.</summary>
internal static class Pkce
{
    public const string Method = "S256";

    /// <summary>A challenge is the base64url of a SHA-256 hash: 43 characters.</summary>
    public static bool IsChallenge(string? challenge) =>
        challenge is { Length: 43 } && challenge.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>A verifier is 43 to 128 characters of A-Z a-z 0-9 - . _ ~ (RFC 7636 section 4.1).</summary>
    public static bool IsVerifier(string? verifier) =>
        verifier is { Length: >= 43 and <= 128 } && verifier.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~');

    /// <summary>Whether <paramref name="verifier"/> hashes to <paramref name="challenge"/>, compared in constant time.</summary>
    public static bool Verify(string? verifier, string challenge)
    {
        if (!IsVerifier(verifier))
        {
            return false;
        }

        var expected = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier!)));
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(challenge));
    }
}
