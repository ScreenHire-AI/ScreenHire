using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ScreenHire.Api.Auth;

/// <summary>
/// Issuer validation for Microsoft's <c>common</c> endpoint. Its discovery document names a placeholder issuer
/// (<c>.../{tenantid}/v2.0</c>), and each token's issuer is the signing-in account's own tenant, so the issuer must
/// match the token's <c>tid</c> claim exactly.
/// </summary>
internal static class MicrosoftIssuer
{
    public static string Validate(string issuer, SecurityToken token, TokenValidationParameters parameters)
    {
        var tenantId = token is JsonWebToken jwt && jwt.TryGetPayloadValue<string>("tid", out var tid) ? tid : null;
        return tenantId is not null && Guid.TryParse(tenantId, out _) && issuer == Expected(tenantId)
            ? issuer
            : throw new SecurityTokenInvalidIssuerException("The token's issuer does not match its Microsoft tenant.");
    }

    public static string Expected(string tenantId) => $"https://login.microsoftonline.com/{tenantId}/v2.0";
}
