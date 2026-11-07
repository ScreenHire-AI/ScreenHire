---
type: Design
title: "OAuth sign-in for the MCP server"
description: "How an AI assistant that signs in only with OAuth, such as a claude.ai custom connector, connects: the discovery documents, dynamic client registration, the authorization code flow with PKCE, the consent page, a connection that is a key row with an API key's scopes, opaque hashed tokens that work on the MCP endpoint only, refresh rotation, revocation, the rate limits and the daily purge."
tags: [mcp, oauth, api-keys, auth]
sources:
  - resource: apps/api/OAuth/OAuthMetadata.cs
  - resource: apps/api/OAuth/OAuthEndpoints.cs
  - resource: apps/api/OAuth/OAuthConsentEndpoints.cs
  - resource: apps/api/OAuth/OAuthServer.cs
  - resource: apps/api/OAuth/OAuthAuthenticationHandler.cs
  - resource: apps/api/OAuth/OAuthTokens.cs
  - resource: apps/api/OAuth/OAuthScopes.cs
  - resource: apps/api/OAuth/OAuthRedirects.cs
  - resource: apps/api/OAuth/OAuthSetup.cs
  - resource: apps/api/Configuration/OAuthOptions.cs
  - resource: apps/api/Contracts/OAuthContracts.cs
  - resource: apps/api/Auth/ApiKeyAuthentication.cs
  - resource: src/ScreenHire.Domain/Organizations/OAuthGrants.cs
  - resource: src/ScreenHire.Infrastructure/Identity/OAuthClient.cs
  - resource: src/ScreenHire.Application/OAuth/OAuthRetention.cs
  - resource: apps/dashboard/src/routes/(auth)/oauth/authorize/+page.svelte
  - resource: apps/dashboard/src/routes/(app)/settings/mcp-server/+page.svelte
  - resource: tests/ScreenHire.IntegrationTests/OAuthServerTests.cs
  - resource: tests/ScreenHire.UnitTests/Auth/OAuthRulesTests.cs
---
# OAuth sign-in for the MCP server

A client that signs in only with OAuth, such as a claude.ai custom connector, cannot paste an API key. The API therefore
runs a small OAuth 2.1 authorization server for the [MCP server](mcp-server.md) (ADR-0027). A person approves the
assistant in the dashboard, and the assistant then works through `/v1/mcp` with the same scopes, rate limit and audit trail
as an API key ([sign-in and tenancy](sign-in-and-tenancy.md), "API keys").

## The flow

1. The assistant calls `/v1/mcp` with no token and gets 401 with
   `WWW-Authenticate: Bearer resource_metadata="https://<api host>/.well-known/oauth-protected-resource/v1/mcp"`.
2. It reads that document (RFC 9728): `resource` is `https://<api host>/v1/mcp`, the one authorization server is the API's
   origin, and `scopes_supported` is `read`, `write`, `verification_read`.
3. It reads `/.well-known/oauth-authorization-server` (RFC 8414; `/.well-known/openid-configuration` answers the same
   document). Endpoints: authorization on the dashboard origin at `/oauth/authorize`, and token, registration and
   revocation on the API at `/v1/oauth/token`, `/v1/oauth/register` and `/v1/oauth/revoke`. Only the S256 PKCE method,
   public clients (`token_endpoint_auth_methods_supported: ["none"]`) and `authorization_response_iss_parameter_supported`.
4. It registers itself (RFC 7591) and gets a `client_id`. Each redirect address must be HTTPS, or HTTP on `localhost` or a
   loopback address; a request must name a registered address exactly, and only a loopback address's port may differ.
5. It opens the authorization URL in the person's browser. The dashboard's consent page (`/oauth/authorize`) needs a
   session: a signed-out person is sent to sign-in and back with the request intact (an emailed link keeps it too).
6. The page shows who is asking, the host it will send the person back to (with a warning for an assistant on their own
   computer), the organization, and what it asks for. Read is always granted. Write and Verification read are checkboxes
   that start unchecked, and a scope the person does not hold themselves is disabled with its reason. Approve or Deny
   sends the browser back to the assistant with `code`, `state` and `iss`, or `error=access_denied`.
7. The assistant posts the code with its PKCE `code_verifier` to the token endpoint and gets an access token, a refresh
   token and the granted `scope`.
8. It calls `/v1/mcp` with `Authorization: Bearer sho_...`.

The consent page calls two session routes: `GET /v1/oauth/authorization-request` (checks the request and says what to show;
a bad client or address is a 400 whose detail the page shows, and nothing redirects to an unchecked address) and
`POST /v1/oauth/authorize` (the decision; antiforgery applies). Both need the permission that makes an API key,
`ManageApiKeys`, and all-jobs access (ADR-0015), because a connection reads every job.

## What a connection is

Approving creates an `api_keys` row (`OAuthClientId` set) with the client's name, the approved scopes, and the approver as
its maker. Its secret is generated and discarded, so it never signs in as an `shk_` key. It follows that tenancy, the
rate limit, `HasApiKeyScope`, `HiringAuthorization.KeyRefusal`, the timeline's actor, the `ReadByKey` log of verification
reads and change history all work unchanged: the assistant shows in activity under its own name.

- **Scopes are an API key's.** `read`, `write` and `verification_read` map to Read, Write and VerificationRead. Write is
  open only to a person whose role holds every hiring write, and Verification read only to an Owner
  (`GrantVerificationAccess`), the rules that create a key (ADR-0011, ADR-0021). The consent route checks again on submit and
  keeps only scopes the assistant asked for.
- **Approving again replaces.** claude.ai registers a new client on every connection, so approving revokes the same person's
  earlier connections in the organization to an assistant with the same name and redirect addresses.
- **Settings > MCP server lists them** with who connected each, access and last use. An Owner or Admin disconnects one
  (`POST /v1/organization/oauth-connections/{id}/revoke`); the row stays, marked revoked. The API keys page lists no
  connection. Removing a member offers to disconnect the assistants they connected.

## Tokens

| Token | Marker | Lifetime | Use |
|---|---|---|---|
| Authorization code | `shc_` | 60 seconds | Once. Bound to the client, the redirect address and the PKCE challenge. |
| Access token | `sho_` | 1 hour | On `/v1/mcp` only. |
| Refresh token | `shr_` | 30 days | Once: each exchange returns a new pair. |

Each is `{marker}_{organization}_{256 random bits}` in lower-case hex and stored only as a SHA-256 hash in `oauth_codes` or
`oauth_tokens`. The organization is in the string so authentication sets the tenant first and the lookup runs under
row-level security, exactly as an API key's does. Opaque rather than JWT: the server that issues a token checks it, so
nothing is gained by signing, and revocation takes effect on the next request.

- **Code exchange** consumes the code with one conditional `UPDATE`, so two exchanges cannot both win; a second one gets
  `invalid_grant` and revokes the connection's tokens. It then checks the client, the exact redirect address, the PKCE
  verifier (constant-time), the optional `resource` (RFC 8707; it must be this MCP server) and that the connection is not
  revoked. A failed check leaves the code used.
- **Refresh** rotates. Presenting a refresh token that was already used, or after revocation, is `invalid_grant`, and a
  reused one revokes every token of its connection, because it may have leaked.
- **Revocation** (RFC 7009) of a refresh token ends the connection's tokens; of an access token, that token. An unknown token
  is not an error.
- **Audience.** `McpCredentials` is a policy scheme: a `Bearer sho_` token goes to `OAuthAuthenticationHandler`, anything else
  to the API key handler. Only the three `mcp*` policies use it, and the hiring API's key handler refuses an `sho_` token,
  so an OAuth token works on the MCP endpoint and nowhere else. The handler counts failures against the address in the key
  handler's `FailedAttempts` and answers 429 when it runs out.
- **Why not OpenIddict.** It finds a reference token by its hash alone, so its tables would sit outside row-level security
  (ADR-0027).

## Limits and settings

`ScreenHire:OAuth:*` (`OAuthOptions`): `AccessTokenLifetime`, `RefreshTokenLifetime`, `CodeLifetime`, `RegistrationsPerHour`
(per address, 20), `TokenRequestsPerMinute` (per address, 60) and `MaxUnapprovedClients` (2000; past it registration answers
429). Counts are per API instance. Only the API reads them.

Discovery, registration, token and revocation allow any origin without credentials (`oauth-protocol` CORS policy), so a
browser-based client can sign in. The consent and management routes are same-origin and session-only.

A daily worker job, `PurgeExpiredOAuthData`, deletes registrations nobody approved within a day, then each organization's
codes and tokens a day past their expiry (a used refresh token stays until it would have expired, for reuse detection).

`oauth_clients` has no organization and no row-level security: a client registers before anyone signs in, and the row holds
only a name and its redirect addresses. `oauth_codes` and `oauth_tokens` are tenant tables with the usual policy.

## Not built

Client ID Metadata Documents (the metadata does not claim `client_id_metadata_document_supported`, so Claude registers),
client secrets, the `client_credentials` grant, ID tokens, and connecting an assistant as a member who sees only their own
jobs (a connection reads every job, as a key does).

## Tests

| Test | Proves |
|---|---|
| `OAuthServerTests` (integration) | both discovery documents; the 401 challenge; registration rules; the whole flow to a working MCP client with Read, Write and Verification read grants (and the review log under the connection); a person cannot grant more than they hold; a recruiter cannot connect; code reuse, wrong verifier, redirect, client and resource fail; refresh rotates and reuse revokes; an OAuth token is refused on the hiring API and on session routes; another organization's data is unreachable; the timeline names the assistant; revoking a connection stops its tokens; connecting twice leaves one; deny returns `access_denied`; CORS preflight |
| `OAuthTokensTests`, `PkceTests`, `OAuthRedirectsTests`, `OAuthScopesTests`, `OAuthMetadataTests` (unit) | token format and kinds, PKCE (RFC 7636 example), redirect rules, scope parsing, the metadata and the challenge |
