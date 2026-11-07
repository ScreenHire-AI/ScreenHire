using System.Text.Json.Serialization;
using ScreenHire.Api.Configuration;
using ScreenHire.Api.Mcp;

namespace ScreenHire.Api.OAuth;

/// <summary>
/// The two discovery documents an MCP client reads before it signs in. The MCP server is the protected resource
/// (RFC 9728), the API's own origin is the authorization server (RFC 8414), and the consent page is on the dashboard's
/// origin, where the sign-in cookie is (docs/architecture/oauth-server.md). Every URL comes from
/// <see cref="DomainOptions"/>, never from the request, so a spoofed Host header changes nothing.
/// </summary>
internal static class OAuthMetadata
{
    public const string ProtectedResourcePath = "/.well-known/oauth-protected-resource/v1/mcp";
    public const string AuthorizationServerPath = "/.well-known/oauth-authorization-server";
    public const string OpenIdConfigurationPath = "/.well-known/openid-configuration";
    public const string RegisterPath = "/v1/oauth/register";
    public const string TokenPath = "/v1/oauth/token";
    public const string RevokePath = "/v1/oauth/revoke";

    /// <summary>The dashboard route of the consent page.</summary>
    public const string ConsentPath = "/oauth/authorize";

    /// <summary>The resource identifier: the MCP server's URL, which a client names in its <c>resource</c> parameter.</summary>
    public static string Resource(DomainOptions domains) => domains.ApiUrl(McpSetup.Path).ToString();

    public static string Issuer(DomainOptions domains) => domains.GetApiOrigin().GetLeftPart(UriPartial.Authority);

    public static string ProtectedResourceUrl(DomainOptions domains) => domains.ApiUrl(ProtectedResourcePath).ToString();

    public static ProtectedResourceMetadata ProtectedResource(DomainOptions domains) => new(
        Resource(domains),
        [Issuer(domains)],
        OAuthScopes.Supported,
        ["header"],
        "ScreenHire");

    public static AuthorizationServerMetadata AuthorizationServer(DomainOptions domains) => new(
        Issuer(domains),
        domains.AppUrl(ConsentPath).ToString(),
        domains.ApiUrl(TokenPath).ToString(),
        domains.ApiUrl(RegisterPath).ToString(),
        domains.ApiUrl(RevokePath).ToString(),
        OAuthScopes.Supported,
        ["code"],
        ["authorization_code", "refresh_token"],
        [Pkce.Method],
        ["none"],
        true);
}

/// <summary>RFC 9728 protected resource metadata.</summary>
internal sealed record ProtectedResourceMetadata(
    [property: JsonPropertyName("resource")] string Resource,
    [property: JsonPropertyName("authorization_servers")] IReadOnlyList<string> AuthorizationServers,
    [property: JsonPropertyName("scopes_supported")] IReadOnlyList<string> ScopesSupported,
    [property: JsonPropertyName("bearer_methods_supported")] IReadOnlyList<string> BearerMethodsSupported,
    [property: JsonPropertyName("resource_name")] string ResourceName);

/// <summary>RFC 8414 authorization server metadata. Public clients only, PKCE S256 only, no client secrets.</summary>
internal sealed record AuthorizationServerMetadata(
    [property: JsonPropertyName("issuer")] string Issuer,
    [property: JsonPropertyName("authorization_endpoint")] string AuthorizationEndpoint,
    [property: JsonPropertyName("token_endpoint")] string TokenEndpoint,
    [property: JsonPropertyName("registration_endpoint")] string RegistrationEndpoint,
    [property: JsonPropertyName("revocation_endpoint")] string RevocationEndpoint,
    [property: JsonPropertyName("scopes_supported")] IReadOnlyList<string> ScopesSupported,
    [property: JsonPropertyName("response_types_supported")] IReadOnlyList<string> ResponseTypesSupported,
    [property: JsonPropertyName("grant_types_supported")] IReadOnlyList<string> GrantTypesSupported,
    [property: JsonPropertyName("code_challenge_methods_supported")] IReadOnlyList<string> CodeChallengeMethodsSupported,
    [property: JsonPropertyName("token_endpoint_auth_methods_supported")] IReadOnlyList<string> TokenEndpointAuthMethodsSupported,
    [property: JsonPropertyName("authorization_response_iss_parameter_supported")] bool AuthorizationResponseIssParameterSupported);
