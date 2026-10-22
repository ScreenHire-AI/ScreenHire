# ADR-0011: Organization API keys now, OAuth client credentials later

- **Status:** Accepted (2026-10-22)
- **Deciders:** Owner
- **Builds on:** ADR-0001 (public API from day one), ADR-0008 (Identity and cookie auth; OpenIddict "when standard OIDC
  tokens are needed")
- **Detail:** `docs/architecture/sign-in-and-tenancy.md`, "API keys"

## Context

The public API is part of the positioning, but every route needed the dashboard's session cookie, so no other system
could call it. Integrators (an HRIS, a spreadsheet script, a no-code automation) need a credential that belongs to the
organization, not to a person, and that survives that person leaving.

Two standard shapes fit:

- **API keys**: a long random secret the organization creates in its settings and sends as a bearer token. Every ATS
  integrators already use works this way. Nothing to run besides one table and one authentication handler.
- **OAuth 2.0 client credentials**: a client id and secret exchanged at a token endpoint for a short-lived access
  token. Needs an authorization server (OpenIddict under ADR-0008), signing keys and their rotation, and a token
  endpoint for every integrator to call first.

OAuth earns its cost when third parties build apps that many organizations install, which needs consent screens and
per-app scopes. ScreenHire has no marketplace and no third-party apps yet.

## Decision

1. **Organization API keys now.** Owners and admins create, list and revoke them in a session. A key is
   `shk_{prefix}_{organization}_{secret}` with a 256-bit secret; only its SHA-256 hash is stored, and the key is shown
   once. Keys are revoked, never deleted.
2. **An ASP.NET Core authentication scheme**, not middleware. A policy scheme forwards to the key handler when the
   request has an `Authorization` header and to the cookie otherwise, so a request is judged by one credential. The
   organization in the key becomes the tenant before the lookup, so the lookup and everything after it run under
   row-level security like a session.
3. **Keys work on the hiring API only**: jobs, applications, candidates and templates, behind one authorization
   policy. Sign-in, onboarding, key management, organization settings, integrity review, automations, distribution
   and the real-time hub stay session-only. Integrity review needs a named person; the others can open to keys one
   area at a time.
4. **Two scopes**, `Read` and `Write`; `Write` includes reading and a key always has `Read`. Writes by a key record no
   acting user, like the system.
5. **A per-key rate limit** with ASP.NET Core's rate limiter: a fixed window per key, the deployment's
   `ScreenHire:ApiKeys:PermitsPerMinute` by default, lowerable per key.
6. **OAuth client credentials later**, through OpenIddict, when third-party apps arrive. Keys stay for an
   organization's own scripts.

## Consequences

- One new tenant table (`api_keys`) with the usual `tenant_isolation` policy; no cross-tenant lookup exists, because
  the key names its organization.
- The organization id inside a key is not secret. It already appears in API responses, and knowing it gets nothing
  without the secret.
- The rate limit counts per API instance. A second replica doubles a key's budget until the limiter moves to a shared
  store.
- Audit entries for key writes have no person. Recording the key as the actor is a later change to the event model.
- There are no screens yet; the routes are in the OpenAPI document and the generated client for the settings page that
  comes later.
