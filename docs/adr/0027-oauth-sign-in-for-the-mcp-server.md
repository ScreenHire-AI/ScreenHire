---
type: Decision
title: "ADR-0027: OAuth sign-in for the MCP server"
description: "The MCP server also accepts OAuth access tokens, so a client that signs in only with OAuth, such as a claude.ai custom connector, can connect: an authorization server on the API (discovery, dynamic client registration, authorization code with PKCE, rotating refresh tokens), a consent page in the dashboard, a connection that is a key row holding the same scopes as an API key, opaque hashed tokens that work on the MCP endpoint only, written by hand rather than with OpenIddict because its tables sit outside row-level security."
tags: [adr, mcp, oauth, auth, api-keys]
---
# ADR-0027: OAuth sign-in for the MCP server

- **Status:** Accepted (2026-11-07)
- **Deciders:** Owner
- **Amends:** ADR-0011 decision 6 (OAuth client credentials later, through OpenIddict). The MCP server's need for OAuth
  arrived before any third-party app, and it needs the authorization code flow, not client credentials.
- **Builds on:** ADR-0011 (API keys), ADR-0021 (the Verification read scope), ADR-0014 (permissions), ADR-0015 (job
  access), ADR-0008 (licence policy; OpenIddict named as the route for OIDC tokens)
- **Detail:** `docs/architecture/oauth-server.md`

## Context

A power user connects an AI assistant to ScreenHire through the MCP server (`docs/architecture/mcp-server.md`). Claude Code
connects with an API key in a header. A client that signs in only with OAuth cannot: a claude.ai custom connector sends a
person through an authorization server and never offers a place to paste a key. ADR-0011 put OAuth off until third-party
apps arrive. None has, but this client has, and the MCP specification now asks every remote server that wants OAuth clients
to publish protected resource metadata (RFC 9728), run or name an authorization server (RFC 8414) with PKCE, and take a
client's registration (RFC 7591) or its metadata document.

The rules that already hold stay put. A key belongs to an organization, carries Read, Write or Verification read, and
only an Owner gives Verification read. A person may not give a key more than their own role has. Every change a key makes is
on the timeline under its name, and every verification read is in the review log. A tenant table is under row-level
security (CLAUDE.md non-negotiable 6).

## Decisions

1. **The MCP server accepts an OAuth access token beside an API key.** The API is the authorization server: discovery
   documents at `/.well-known/oauth-protected-resource/v1/mcp` and `/.well-known/oauth-authorization-server`, dynamic
   client registration, an authorization code flow with PKCE (S256 only), a token endpoint with rotating refresh tokens,
   and revocation. Clients are public: no client secret exists. A 401 from `/v1/mcp` carries `resource_metadata`, which is
   how a client finds all of this.
2. **The server is written here, not with OpenIddict.** OpenIddict (Apache-2.0) passes ADR-0008 and ADR-0011 names it, but
   it finds a reference token by its hash alone, so its token and authorization tables must sit outside row-level
   security although every row belongs to one organization. An API key names its organization inside the key, so the
   handler sets the tenant first and the lookup runs under the same policy as every tenant query. Smaller reasons: its
   signing and encryption credentials need rotation across API instances, its principal needs a bridge into the identity
   that every policy and audit column already expects, and registration and client metadata documents are custom code on
   either path. The security rules (single-use codes, PKCE, exact redirect matching, refresh rotation with reuse
   detection, audience) each have a test. OpenIddict remains the named route for OIDC ID tokens, client credentials or a
   marketplace of third-party apps; this ADR does not close that.
3. **A connection is an API key row.** Approving a request creates a key row with the client's name, the approved scopes
   and the approver as its maker, marked with its OAuth client. Its secret is generated and thrown away, so it can never
   sign in as an `shk_` key. Authentication, the rate limit, scope checks, the timeline, the verification review log and
   change history therefore treat a connection exactly as a key. Revoking the row ends every token of the connection at
   once. The API keys page does not list connections; Settings > MCP server does.
4. **Tokens are opaque, hashed and name their organization.** Codes (`shc_`), access tokens (`sho_`) and refresh tokens
   (`shr_`) are random, stored as SHA-256 hashes, and begin with a marker and the organization id, as a key does. An
   access token lasts one hour, a refresh token 30 days and is single use, a code 60 seconds and is single use. A code or
   refresh token presented twice revokes the connection's tokens. A JWT would buy nothing, because the server that issues a
   token is the one that checks it, and it could not be revoked at once.
5. **An access token works on `/v1/mcp` and nowhere else.** The spec requires a resource server to accept only tokens meant
   for it. The MCP endpoint reads a token by its marker; the hiring API's key handler refuses it.
6. **Who may connect, and what they may give.** The people who may make an API key (Owner and Admin, with all-jobs access):
   the consent route needs the same permission. A person approves from the dashboard, where their session is, and sees the
   assistant's name, where it will send them back to, the organization, and what it asks for. Read is always granted. Write
   and Verification read are opt-in checkboxes and start unchecked. Write is open only to a person whose role holds every
   hiring write, and Verification read only to an Owner, so a person never gives more than they hold, and the server checks
   again on submit. The organization is the session's current one.
7. **Registration is open and capped.** Registration needs no credential, so it is rate limited per address, capped in
   total, and a registration nobody approves within a day is purged. An assistant such as claude.ai registers a new client
   on every connection, so approving revokes the same person's earlier connections to an assistant of the same name and
   redirect addresses.
8. **Client ID Metadata Documents wait.** The specification now prefers them to registration, but fetching a client's
   metadata URL is a server-side request to a stranger's address and needs its own protections. Claude falls back to
   registration when the server does not advertise them. When registration is dropped from the specification, this is the
   follow-up.

## Consequences

- A claude.ai custom connector connects with the server URL alone, once the API is on a public HTTPS host.
  Locally the flow runs against the integration tests and the MCP client.
- Discovery, registration, token and revocation answer any origin without credentials, so browser-based clients such as the
  MCP Inspector work. The consent and management routes stay same-origin.
- A connection survives its maker leaving, as a key does. Removing a member offers to disconnect the assistants they
  connected, beside the keys they made.
- Two more tenant tables (`oauth_codes`, `oauth_tokens`) have row-level security; `oauth_clients` has none and holds only
  what a client sent. A daily worker job removes expired codes and tokens and unapproved registrations.
- A leaked refresh token is useful once: the owner of the real one finds their next refresh refused, signs in again, and the
  connection's tokens are gone in the meantime.
- Nothing about what verification returns changes. A level is evidence for a person to review and never a reason to reject
  (ADR-0004, ADR-0021).
