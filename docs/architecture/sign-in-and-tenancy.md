---
type: Design
title: "Sign-in, organizations and tenant isolation"
description: "Identity providers, email links and passkeys, abuse controls, API key authentication, configured domains, organizations, permission checks, built-in and custom roles, team members and invitations, job access through each job's hiring team, and tenant isolation through named query filters and Postgres row-level security under screenhire_app, with Dev SSO and Mailpit locally."
tags: [auth, tenancy, rls, team]
sources:
  - resource: apps/api/Auth/Antiforgery.cs
  - resource: apps/api/Auth/ApiKeyAuthentication.cs
  - resource: apps/api/Auth/ApiKeyTokens.cs
  - resource: apps/api/Auth/AuthenticationSetup.cs
  - resource: apps/api/Endpoints/PasskeyEndpoints.cs
  - resource: apps/api/Contracts/PasskeyContracts.cs
  - resource: src/ScreenHire.Infrastructure/Identity/ApplicationUserPasskey.cs
  - resource: src/ScreenHire.Infrastructure/Persistence/ScreenHireDbContext.cs
  - resource: apps/dashboard/src/lib/passkeys.ts
  - resource: apps/dashboard/src/routes/(app)/account/passkeys/+page.svelte
  - resource: apps/dashboard/src/routes/(auth)/sign-in/+page.svelte
  - resource: apps/api/Configuration/AuthOptions.cs
  - resource: apps/api/Auth/EmailVerification.cs
  - resource: apps/api/Auth/ExternalProviders.cs
  - resource: apps/api/Auth/HiringAuthorization.cs
  - resource: apps/api/Auth/Invitations.cs
  - resource: apps/api/Auth/MagicLinks.cs
  - resource: apps/api/Auth/Memberships.cs
  - resource: apps/api/Auth/MicrosoftIssuer.cs
  - resource: apps/api/Auth/RoleAccess.cs
  - resource: apps/api/Auth/ReturnUrls.cs
  - resource: apps/api/Auth/SecuritySchemes.cs
  - resource: apps/api/Auth/TenantResolutionMiddleware.cs
  - resource: src/ScreenHire.Domain/Organizations/RolePermissions.cs
  - resource: apps/api/Endpoints/AuthEndpoints.cs
  - resource: apps/api/Endpoints/TeamEndpoints.cs
  - resource: apps/api/Endpoints/RoleEndpoints.cs
  - resource: apps/api/Endpoints/ApiKeyEndpoints.cs
  - resource: apps/api/Contracts/ApiKeyContracts.cs
  - resource: apps/dashboard/src/routes/(app)/settings/api-keys/+page.svelte
  - resource: apps/api/Endpoints/VerificationEndpoints.cs
  - resource: apps/api/Endpoints/HiringTeamEndpoints.cs
  - resource: apps/api/Hiring/HiringTeams.cs
  - resource: apps/api/Configuration/DomainOptions.cs
  - resource: src/ScreenHire.Domain/Organizations/ApiKey.cs
  - resource: src/ScreenHire.Domain/Organizations/CustomRole.cs
  - resource: src/ScreenHire.Domain/Organizations/ITenantOwned.cs
  - resource: src/ScreenHire.Domain/Organizations/IJobScoped.cs
  - resource: src/ScreenHire.Domain/Organizations/JobAccess.cs
  - resource: src/ScreenHire.Domain/Organizations/MembershipEvent.cs
  - resource: src/ScreenHire.Domain/Organizations/Organization.cs
  - resource: src/ScreenHire.Domain/Organizations/OrganizationInvitation.cs
  - resource: src/ScreenHire.Domain/Organizations/OrganizationMember.cs
  - resource: src/ScreenHire.Domain/Organizations/TeamPolicy.cs
  - resource: src/ScreenHire.Infrastructure/Persistence/Configurations/CustomRoleConfiguration.cs
  - resource: src/ScreenHire.Infrastructure/Tenancy/ITenantContext.cs
  - resource: src/ScreenHire.Infrastructure/Tenancy/TenantSecurity.cs
  - resource: src/ScreenHire.Infrastructure/Tenancy/TenantSessionInterceptor.cs
  - resource: apps/apphost/DevSso/config.json
  - resource: apps/dashboard/src/lib/auth.ts
  - resource: apps/dashboard/src/lib/permissions.ts
  - resource: apps/dashboard/src/lib/settings.ts
  - resource: apps/dashboard/src/lib/integrations.ts
  - resource: apps/dashboard/src/lib/components/settings-menu.svelte
  - resource: apps/dashboard/src/lib/hiring-team.ts
  - resource: apps/dashboard/src/lib/components/member-access-sheet.svelte
  - resource: apps/dashboard/src/lib/components/hiring-team-sheet.svelte
  - resource: apps/dashboard/src/lib/components/member-jobs.svelte
  - resource: apps/dashboard/src/routes/(app)/settings/team/+page.svelte
  - resource: apps/dashboard/src/lib/moves.svelte.ts
  - resource: apps/dashboard/src/lib/components/new-role-sheet.svelte
  - resource: apps/dashboard/src/routes/(app)/settings/roles/+page.svelte
  - resource: apps/dashboard/src/routes/(app)/settings/roles/[roleId]/+page.svelte
  - resource: apps/dashboard/src/routes/(app)/settings/roles/compare/+page.svelte
  - resource: src/ScreenHire.Infrastructure/Persistence/Configurations/CandidateConfiguration.cs
---
# Sign-in, organizations and tenant isolation

How a recruiter signs in, how their organization is chosen, and how every tenant-owned query stays inside that
organization. Decisions: ADR-0003 (domains are configuration), ADR-0008 (Identity, cookie auth, named query filters
plus RLS). Non-negotiables 6 and 7 in CLAUDE.md.

## Sign-in

There are no passwords. A person signs in with an identity provider, an emailed link or a passkey, and the first sign-in
creates their account (a passkey is added later, from a signed-in session). ASP.NET Core Identity is the user store
(`ApplicationUser`, tables `users`, `user_logins`, `user_claims`, `user_tokens`, `user_passkeys`); the session is the `sh.session` cookie (HttpOnly, SameSite=Lax, 14 days sliding).

Sign-in keeps no provider tokens. A member who connects a Google or Outlook calendar for interview scheduling goes through
a separate OAuth flow with its own scopes and stored tokens, and removing the member deletes that connection and takes them off the interviews still to come
([interview scheduling](interview-scheduling.md)).

The dashboard calls the API same-origin through its `/v1` path (the Vite proxy locally), so cookies are host-only on
`ScreenHire:Domains:AppHost` and need no CORS. Every route requires a signed-in user unless it calls
`.AllowAnonymous()` (a fallback authorization policy in `apps/api/Auth/AuthenticationSetup.cs`). The hiring API also accepts an
organization API key in place of the session ("API keys" below).

| Route | What it does |
|---|---|
| `GET /v1/auth/providers` | The configured providers and whether email links work. Issues the antiforgery token. |
| `GET /v1/auth/me` | The signed-in user and their organization, with their role, every permission it gives them and whether they see all jobs, or 401. Issues the antiforgery token either way. |
| `GET /v1/auth/challenge/{provider}?returnUrl=` | Redirects to the provider (authorization code + PKCE). |
| `GET /v1/auth/signin-{provider}` | The handler's redirect URI. The provider returns here with a code. |
| `GET /v1/auth/callback?returnUrl=` | Links or creates the user, signs them in, redirects to the dashboard. |
| `POST /v1/auth/magic-link` | Emails a sign-in link. Always 202, known address or not. An optional `returnUrl` (a local path, sanitized as above) rides in the link's fragment beside the token, so the verify page opens it after sign-in; the OAuth consent page relies on it. |
| `POST /v1/auth/magic-link/verify` | Redeems a link's token and signs in. |
| `POST /v1/auth/onboarding` | Creates the user's organization (name and slug) with them as Owner. |
| `POST /v1/auth/invitations/accept` | Redeems an emailed invitation: signs in as the invited address and joins its organization. |
| `POST /v1/auth/invitations/lookup` | Names the organization and role an open invitation offers, for the accept page. It uses nothing up, and answers an unusable token as accepting does. |
| `GET /v1/auth/organizations` | Every organization the signed-in user belongs to, with their role in each. A custom role is named only in the one they work in: row-level security hides other organizations' roles. |
| `POST /v1/auth/switch-organization` | Makes another of the user's organizations the one they work in. |
| `POST /v1/auth/sign-out` | Ends the session. |
| `POST /v1/auth/passkeys/sign-in/options` | Anonymous. The WebAuthn request options for a sign-in with no username, as `{ optionsJson }`. The challenge waits in the `sh.passkey` cookie. |
| `POST /v1/auth/passkeys/sign-in` | Anonymous. Checks the browser's answer and signs in with 204, or answers 401 for any failure. |
| `GET /v1/auth/passkeys` | The signed-in person's passkeys: id, name, created, last used, and the limit. |
| `POST /v1/auth/passkeys/register/options`, `POST /v1/auth/passkeys/register` | Signed in. The creation options, then the browser's answer with a name for the device. 409 at ten passkeys. |
| `DELETE /v1/auth/passkeys/{id}` | Signed in. Removes one of the person's own passkeys, or 404 for any other id. 409 when it is their last way to sign in. |

### Identity providers

`ScreenHire:Auth:Google`, `ScreenHire:Auth:Microsoft` and `ScreenHire:Auth:Oidc:<id>` (any OpenID Connect provider,
for customer SSO later) each take `ClientId` and `ClientSecret` from user secrets or the host's secret store. A
provider without both is not registered and does not appear in `GET /v1/auth/providers`. None are configured in the
repository.

- The handler uses the authorization code flow with PKCE and `response_mode=query`, so the return trip is a top-level
  GET and SameSite=Lax correlation cookies arrive.
- The `redirect_uri` is `{AppOrigin}/v1/auth/signin-{id}`, built from `ScreenHire:Domains`, not from the request,
  because the request reaches the API through the dashboard's proxy.
- Microsoft uses the `common` endpoint (personal and work accounts) with an issuer check against the token's `tid`.
- A new identity links to an existing user with the same email only when the provider vouches for the address:
  `email_verified` for Google and generic providers; for Microsoft, the `xms_edov` optional claim (work accounts; the
  app registration must request it) or the personal-account tenant. Otherwise sign-in stops with
  `/sign-in?error=email-unverified`. An identity that is already linked always signs in.
- Return URLs are local paths only (`ReturnUrls.Sanitize`).

### Email links

`MagicLinks` (`apps/api/Auth/MagicLinks.cs`) stores only the SHA-256 hash of a 256-bit token, with a 15-minute expiry
and a `consumed_at` that one conditional UPDATE sets, so a link works once even with two clicks at the same moment.
An address gets at most 5 links per 15 minutes; beyond that the reply is the same and no email goes out. The link
opens `/sign-in/verify#token=...` on the dashboard: the token is in the fragment, which browsers never send to a
server, and the person confirms with a button, so a mail scanner that opens the link cannot use it up. Identity's
own token providers are stateless and need an existing user, which is why this is a table.

Email goes through `IEmailSender` (`src/ScreenHire.Infrastructure/Email`). The only adapter is SMTP via MailKit,
registered when `ConnectionStrings:mailpit` is set; without it the magic-link form is hidden. The production
provider is a later decision. The magic-link request sends inside the request, after its commit, so the reply
waits for SMTP; moving the send to a worker message is a later change.

### Passkeys

A passkey (WebAuthn) signs a person in with their device's own unlock and nothing typed, which suits people who sign in once
a week and would otherwise wait for an email. ADR-0008 chose `SignInManager`'s passkey support with our own endpoints
(`apps/api/Endpoints/PasskeyEndpoints.cs`), because only the Blazor template wires them. The dashboard side is
`apps/dashboard/src/lib/passkeys.ts` (the browser's own WebAuthn calls, no library) and the Passkeys page in the person's
menu (`/account/passkeys`, outside Settings because it is the person's own, not the organization's).

- **Data.** `user_passkeys` holds Identity's row (credential id, user, a JSON `data` column with the public key, name, created
  time and signature counter) and `last_used_at`, which Identity does not keep. It is global like `users`: no
  `organization_id` and no row-level security, because a passkey belongs to the person whatever organizations they are in.
  `ScreenHireDbContext` maps the table itself and `AuthenticationSetup` names the store (`UserOnlyStore` over
  `ApplicationUserPasskey`), and `Stores.SchemaVersion` stays at 1. Setting it to 3 would change the model of the one context
  that has the API's service provider and none of the others (the migrator, `dotnet ef`, the seeder, the tests), so the API's
  model would differ from the migrations' snapshot. `PasskeyModelTests` checks that both builds agree.
- **Relying party.** `IdentityPasskeyOptions.ServerDomain` is the host of `ScreenHire:Domains:AppHost`, and the origin check
  accepts exactly the app origin (scheme, host and port), not the default's "any subdomain". The API is reached through the
  dashboard's `/v1` path, so the request's own host is not what the person's authenticator knows the site by, and a careers
  page on a sibling host must never be able to ask for an assertion.
- **Sign-in with no username.** The button sends no email, so registration requires a resident (discoverable) credential and
  the authenticator offers its passkeys for this site. A credential that is not discoverable would register and then never
  work from the button.
- **User verification is required.** A passkey is the only factor, so the options ask for user verification and the check
  rejects an assertion whose flag is unset: the device must have checked the person (a PIN, a fingerprint, a face), not only
  that something was touched. Platform authenticators and phones always do; a bare security key without a PIN cannot sign in.
- **One credential, one person.** A credential id is the table's key and is registered once. Registering an id that already
  belongs to someone else fails and changes nothing (`PasskeyTests` checks the victim's row and sign-in are untouched).
- **Ceremony state.** The challenge waits in the `sh.passkey` cookie (HttpOnly, SameSite=Lax, five minutes, the session's secure
  policy), so it is bound to the browser that asked. An answer sent without that browser's cookie finds no ceremony: Identity
  throws then, and the endpoints turn that, and a body that is not a credential, into the refusal, logging the exception type
  (never the body). The state is in the cookie, not on the server, so someone who copied both the cookie and the answer
  could send them again within the five minutes.
- **Refusals say nothing.** Every sign-in failure is the same 401: no challenge, wrong origin, bad signature, an unknown
  passkey, a locked-out user. It never says whether a credential exists or whose it is.
- **Counters and last use.** After a sign-in the store keeps the passkey's new signature counter (a cloned authenticator shows
  as a counter that went backwards) and the endpoint stamps `last_used_at`.
- **The last way in.** Removing a passkey is refused (409) when it is the person's last one and nothing else signs them in: no
  identity provider that is still configured and no emailed link (`PasskeyLimits.MayRemove`). With email links working it is
  always allowed, because the address is verified at sign-up. The count and the delete run under one per-user advisory lock,
  so two removals at once cannot both see two passkeys and leave none.
- **Registration needs a session.** A signed-in session can add a passkey, with no step-up check in v1. A stolen session could
  therefore add one that outlives it. The mitigations are that the person sees every passkey on their page and can remove it,
  and that there are at most ten (counted under the same per-user lock as a removal, so two adds at once cannot pass it).
  Asking for a fresh sign-in before adding one is the next step if the threat grows.
- **Sessions.** Adding or removing a credential may rotate the security stamp, which would sign the person out of their own
  session within the validator's interval, so the endpoints refresh the sign-in when the stamp changed. Removing a passkey
  does not end other sessions.
- **Abuse.** Antiforgery on every POST and DELETE. The two anonymous routes have their own per-IP limit,
  `ScreenHire:Auth:PasskeyPermitsPerMinute` (default 20), which Development does not raise, so each test uses a unique
  `X-Forwarded-For`. The rest use the `auth` limit. A request body over 64 KB is a 413 before it is read, and a
  credential over 20,000 characters is a 400.
- **Phishing.** A passkey is bound to the relying party id, so a look-alike site cannot ask for an assertion for it. That is
  the reason to offer it beside the email link.

### Abuse controls

- **Antiforgery.** Every POST under `/v1/auth` needs the `X-XSRF-TOKEN` header. The API puts the token in the
  readable `XSRF-TOKEN` cookie on `providers` and `me`, and the generated client's `apiFetch` echoes it on every
  non-GET request. Tokens are bound to the user, so sign-in and sign-out re-issue them.
- **Rate limit.** A fixed window per client IP on the challenge and every auth POST, `ScreenHire:Auth:PermitsPerMinute`
  (default 100). Development raises it to 2000 (`apps/api/appsettings.Development.json`): every test signs in
  from 127.0.0.1 against one API, and the integration suite alone sends about 80 such requests in 20 seconds.
  The per-address email cap is unchanged everywhere and has its own test. It counts under a per-address advisory
  lock, so simultaneous requests cannot slip past it. The client IP comes from `X-Forwarded-For`
  (`UseForwardedHeaders`), believed only from loopback and from the addresses or CIDR networks in `ScreenHire:Auth:TrustedProxies`, reading
  `ScreenHire:Auth:ForwardLimit` entries from the right.
  Loopback covers the dashboard's own `/v1` proxy (`xfwd: true` in `vite.config.ts`). A deployed reverse proxy must
  be listed, or every visitor shares its bucket.
- **Email addresses.** The magic-link form takes one bare address. A display name (`Name <a@example.com>`) or a list
  is refused, so nobody gets a fresh per-address cap by varying the display name.

## API keys

Integrators call the hiring API with an organization API key instead of a session (ADR-0011). A role with Manage
API keys (Owners and Admins) manages keys in a session, on the dashboard's Integrations > API keys page, which shows a new key once.

| Route | What it does |
|---|---|
| `GET /v1/organization/api-keys` | The organization's keys, revoked ones too: prefix, name, scopes, rate limit, created, last used, revoked, and who created it. |
| `POST /v1/organization/api-keys` | Creates a key and returns it once, with `Cache-Control: no-store`. The `VerificationRead` scope needs the Owner-only Give a key verification access permission, otherwise 403 "Owners only". |
| `GET /v1/organization/api-keys/{id}` | One key, without the key itself. |
| `POST /v1/organization/api-keys/{id}/revoke` | Revokes it. The next request with it gets 401. |
| `GET /v1/organization/api-keys/history`, `GET /v1/organization/api-keys/last-update` | The page's change history: each key created and revoked, with its name, access and who, never its hash or prefix (`docs/architecture/change-history.md`). |

- **The key** is `shk_{prefix}_{organization}_{secret}`, lower-case hex: 8 digits of prefix that the list shows, the
  organization's id and a 256-bit secret (`apps/api/Auth/ApiKeyTokens.cs`). `api_keys` stores the SHA-256 hash of the
  whole key, never the key, and nothing logs it. Keys are revoked, never deleted: `screenhire_app` has no `DELETE` on
  the table.
- **Authentication** is the `ApiKey` scheme (`apps/api/Auth/ApiKeyAuthentication.cs`) for `Authorization: Bearer
  shk_...`. It reads the organization from the key, sets it as the tenant in a DI scope of its own and looks the hash
  up there, so the lookup runs under row-level security like every other tenant query. A key naming the wrong
  organization finds nothing. `last_used_at` is written at most once a minute.
- **Where keys work.** The hiring API (jobs, applications, candidates, templates and reports) uses the `hiring-api`
  authorization policy, whose scheme `SessionOrApiKey` forwards to the key handler when the request has an
  `Authorization` header and to the cookie otherwise. A request is judged by one credential: a bad key with a good
  cookie is 401. Every other route keeps the cookie-only default and fallback policies, so a key gets 401 on sign-in,
  onboarding, key management, organization settings, the routes that act on a verification, automations, distribution
  and the hub.
- **Verification only with its scope.** Application and candidate reads send a key `verification: null`, and the
  application timeline leaves out `VerificationAssessed` entries (ADR-0011): levels and reason codes are for the person
  reviewing (ADR-0004), not for another system to reject on. A key with the `VerificationRead` scope an Owner gave it
  (ADR-0021) reads them through two routes only, `GET /v1/applications/{id}/verification` and
  `GET /v1/verification/applications` (applications by level), whose `verification-api` policy admits the session or a
  key with that scope and answers 403 to any other key. Each read through a key adds a `ReadByKey` row to the
  application's verification review log, saved before the answer goes out: the key, the time and no person. Dismissing,
  explaining and noting stay the session's. The MCP server's verification tools use the same reads
  ([MCP server](mcp-server.md)).
- **The MCP server** at `/v1/mcp` lets a team's AI assistant use the hiring API with a key, or with an OAuth connection a
  person approved, and nothing else: its own `mcp` policy authenticates the `McpCredentials` scheme (the `ApiKey` handler,
  or the OAuth handler for an `sho_` access token), so a session cookie gets 401 there ([MCP server](mcp-server.md),
  [OAuth sign-in](oauth-server.md)). A connection is a key row with an OAuth client, so it has a key's scopes, rate limit,
  actor and tenant; an `sho_` token is refused everywhere but `/v1/mcp`, and the API keys page lists no connection. Its write tools check the same scope rule as the routes
  (`HiringAuthorization.KeyRefusal`), its verification tools need the `VerificationRead` scope, and its report and
  saved-candidate tools need only `Read`. The report tool reads what the reports routes read, and the saved-candidate
  tool calls the read service of session-only routes.
- **The tenant** is the key's organization, with no user (`TenantResolutionMiddleware`, which runs after
  authorization because the hiring policy replaces the request's user there). Writes by a key record no user; the
  application timeline records the key as `actorApiKeyId`.
- **Scopes** are `Read`, `Write` and `VerificationRead`. A key always has `Read`; `Write` passes `RequirePermission`
  for the hiring writes (`RolePermissions.HiringWrites`), what a Recruiter does, and a read-only key gets 403
  "Read-only key". Every other permission refuses a key ("Not for API keys"). `VerificationRead` includes nothing and
  `Write` does not include it. Creating a Write key needs every hiring write in the creator's role, and creating a
  `VerificationRead` key needs Give a key verification access, which only the Owner role has, so a key never carries
  more than the person who made it (ADR-0014, ADR-0021). A key's scopes are fixed at creation. A member on Only jobs
  they're on creates no key at all, because a key reads every job (ADR-0015).
- **Antiforgery** is skipped for a request the key scheme authenticated: it carries no cookie to forge with.
- **Rate limit.** The `api-key` rate-limiter policy on the hiring groups gives each key a fixed window per minute of
  `ScreenHire:ApiKeys:PermitsPerMinute` (default 600), or the lower `permitsPerMinute` set on the key, and answers 429
  beyond it. The setting is also the most any key gets: lowering it lowers a key created with more. Sessions pass unlimited. Failed attempts count per client address: beyond
  `ScreenHire:ApiKeys:FailedAttemptsPerMinute` (default 30) the address gets 429 with `Retry-After` for every key until
  the minute is up, without a database lookup. Both counts are per API instance.
- **OpenAPI** declares two security schemes, `session` (the cookie) on every signed-in route and `apiKey` (HTTP
  bearer) as the alternative on the hiring API and the two verification reads (`apps/api/Auth/SecuritySchemes.cs`), so
  Scalar and the generated client show them.
- **The API reference** (Scalar at `/scalar`) and the OpenAPI file (`/openapi/v1.json`) are public in every
  environment, at the same paths: they document the public API and hold no data. `GET /v1/meta` reports the API's
  public origin (`ScreenHire:Domains:ApiHost`), and the dashboard's API keys and Webhooks pages build their base URL and
  reference links from it. It also reports `careersUrlPattern`, a tenant's careers address with `{tenant}` where the slug goes
  (`DomainOptions.CareersUrlPattern`), which onboarding fills in to show the address a name will get. The public page calls no third party: Scalar's telemetry, its CDN fonts, its AI chat and its MCP
  server are off (`apps/api/Program.cs`). `ApiReferenceTests` reaches both with the API in Production and checks the
  first two.

## Domains

`ScreenHire:Domains` (`apps/api/Configuration/DomainOptions.cs`, validated at start) holds `Scheme`, `BaseDomain`,
`AppHost`, `ApiHost`, `CareersDomain`, `CareersHostPattern` and an optional `CookieDomain`. Development defaults are
in `apps/api/appsettings.Development.json`; the AppHost overrides `AppHost`, `ApiHost` and `CareersDomain` with the
real endpoints, because tests use random ports, and gives the careers server the same `CareersDomain` and
`CareersHostPattern` (`{tenant}.{CareersDomain}`) for tenant hosts (`docs/architecture/apply-flow.md` section 6). `AppHost` drives OIDC redirect URIs, emailed links, redirects after sign-in and the
CORS origin. Leave `CookieDomain` empty: a parent-domain cookie would also reach tenant careers pages.

## Organizations and roles

`OrganizationMember` joins a user to an organization with a role: Owner, Admin, Recruiter or Viewer. A user can
belong to several organizations and works in one at a time: the one in `users.active_organization_id` while they are
still a member of it, otherwise the first one they joined. Joining by invitation and `POST /v1/auth/switch-organization`
set it. It is per user, not per session, so it follows them to every device, and switching closes their live-update
connections into the organization they left (`docs/architecture/realtime.md`). A membership also holds the member's own notification
settings (`organization_members.notifications`), so each organization keeps its own (realtime.md, "Teammates' changes
as toasts"). Onboarding creates the organization and the
Owner membership in one transaction, under a per-user advisory lock, so a double submit makes one organization, not
two. A slug is a DNS label (3 to 63 lower-case letters, digits and hyphens) that is not reserved, because it becomes
a careers subdomain. The slug never changes after onboarding: `PUT /v1/organization` (Edit settings) renames the
organization and leaves the slug, because boards and candidates already link to its careers address.
`GET /v1/organization` gives any member the name, slug, home region and `careersUrl`, the careers board built from
`ScreenHire:Domains` (`DomainOptions.CareersBoardUrl`), so the dashboard never builds a careers host itself.

**Permissions, not role names** (ADR-0014). A member has one role: Owner, Admin, Recruiter or Viewer, or `Custom` with
`custom_role_id` naming one of the organization's own roles. `GET /v1/auth/me` returns the role, its name and every
permission it gives. Every route that changes data calls `RequirePermission(Permission.X)`
(`apps/api/Auth/HiringAuthorization.cs`), which reads the member's role through `RoleAccess` on each request, so a role
change, or a change to what a custom role allows, takes effect on the member's next request. A role without the
permission gets 403 "Not in your role", whose detail names the permission ("Your role does not include Move
candidates."). Removing a member also closes their open real-time connections into the organization at once
(`RealtimeConnections`, `docs/architecture/realtime.md`).

**The permission catalogue** (`src/ScreenHire.Domain/Organizations/RolePermissions.cs`) names what a role lets a person
do, in three groups (Hiring, Templates, Organization), and the set each built-in role has. See jobs and candidates is in
every role (`EveryRole`): reads are not permission-checked, and job access will narrow which jobs. Manage Owners,
Give a key verification access and Manage billing ([billing](billing.md)) are Owner-only (`OwnerOnly`): no custom role holds any, and Admin has none. Delete anyone's notes (`NotePolicy`) lets a role delete a note
someone else wrote; Owners and Admins have it. It comes with Add notes and tags, which the delete route asks for first.
Erase candidates (`POST /v1/candidates/{id}/erase`) is held by Owners and Admins and can go in a custom role; the route also
needs a person who sees every job, and refuses organization API keys ([candidates](candidates.md)).

Edit anyone's views (`ViewPolicy`, `saved-views.md`) lets a role rename, share or delete a view someone else saved; Owners and
Admins have it, and so can a custom role, with no other permission needed because a view is a screen preference, not
hiring data.

Create tasks came after custom roles, with the route that creates a task by hand. The built-in roles that write hiring
data have it. It is a hiring write, and a Write key needs every hiring write in its creator's role, so the
`GrantCreateTasksToCustomRoles` migration gave it to each custom role that already held all the other hiring writes;
any other custom role has it only once an Owner adds it. `RolePermissionsTests` reads each guarded route from
`apps/api/Endpoints`, fails when a route has no permission or a different one, and holds the built-in roles to the exact
sets the role-name checks used to allow (plus Create tasks), so no existing member lost access.
`GET /v1/organization/roles` serves the catalogue, with the organization's custom roles, to any member.

**Custom roles** (`custom_roles`, `CustomRole`) start as a copy of a role, built-in or custom, and change on their own
page. A copy never keeps an Owner-only permission, and reading a role drops one again, so a stored row cannot grant it.
Names are 1 to 50 characters, unique in the organization ignoring case (`normalized_name`), and never a built-in role's
name. Built-in roles never change. A role somebody has, or an open invitation offers, cannot be deleted: 409 "Role in
use" says how many. Each create, change and delete runs under the member lock, lands in the membership log and in the
role's change history ([change-history.md](change-history.md)). A custom role's description is generated from its
permissions (`RolePermissions.Summarize`), so it always says what the role allows.

The dashboard follows the same permissions (`apps/dashboard/src/lib/permissions.ts`). **Settings** is three sidebar items, Organization,
Hiring setup and Integrations, each shown for anyone whose role has a permission for at least one page in it and opening the
first page they may use. Under `/settings/*`, Organization holds General, Team, Roles and Data requests; Hiring setup holds Careers site,
Branding, Verification and Email domain; Integrations holds Slack, MCP server, Webhooks and API keys, reached from a directory at
`/settings/integrations` (email templates are on the Templates page, and their old `/settings/email-templates` addresses redirect
there). The first two share the Settings menu, which lists only the pages of the area; each page is behind its permission
(`settingsSections` and `settingsAreas` in `apps/dashboard/src/lib/settings.ts`, the integrations from `integrations` in
`apps/dashboard/src/lib/integrations.ts`), and the command palette lists the same pages. Slack's page and entry are left out wherever `GET /v1/slack` says Slack is not `available` (a feature on its
settings row, `SettingsFeatures`), and so is Email domain's wherever `GET /v1/organization/sending-domain` says
there is no email provider (shown until that answer says so, and kept when the request fails), and an address to either goes to the first page the person may use. The settings layout tells anyone who opens another page by its address that their role cannot change it.
Hiring controls show by the permission their route needs: an Interviewer sees Add note and Fill scorecard but no Move.
The Team page offers only what `TeamPolicy` allows (`settings.ts` mirrors it): an Admin sees no Owner role and no actions
on an Owner's row, and nobody gets actions on their own row. Removing a member offers to revoke the active API keys they
created as well, for a role that manages keys (ADR-0011), and always disconnects the assistants they connected (ADR-0028).
The API stays the judge.

### Roles

`apps/api/Endpoints/RoleEndpoints.cs`, under `/v1/organization/roles`:

| Route | Who | What it does |
|---|---|---|
| `GET /` | Any member | The roles: the built-in ones, most access first, then the organization's by name, each with its permissions, how many people have it and Last updated; and every permission's group, name, description and flags. |
| `GET /{roleId}` | Any member | One custom role, with what it was copied from. Another organization's id is 404. |
| `GET /{roleId}/history` | Any member | The role's change history, newest first. |
| `POST /` | Manage the team | Makes a role by copying `copyFrom` (a built-in role) or `copyFromRoleId` (a custom one). |
| `PUT /{roleId}` | Manage the team | Renames it and sets its permissions. An Owner-only permission, a set without See jobs and candidates, Delete anyone's notes without Add notes and tags, or a name over one line is 400. |
| `DELETE /{roleId}` | Manage the team | Deletes it, or 409 while anyone has it or an open invitation offers it. |

### Team members and invitations

`apps/api/Endpoints/TeamEndpoints.cs`, under `/v1/organization`:

| Route | Who | What it does |
|---|---|---|
| `GET /members` | Any member | The organization's members with their email, name, role and role name, their job access, whether they see all jobs, how many of the jobs the reader sees they are on, and, to a reader who manages the team, `lastUpdated`: the newest role or job access change and who made it (null until one happens). |
| `GET /members/{memberId}/jobs` | Any member | The jobs whose hiring team the member is on, among those the reader sees. |
| `PUT /members/{memberId}/role` | Manage the team | Gives a member another role: a built-in one, or `Custom` with `customRoleId`. |
| `PUT /members/{memberId}/job-access` | Manage the team | Sets `All` or `HiringTeamOnly`. 400 for an Owner or Admin, who see all jobs anyway. |
| `DELETE /members/{memberId}` | Manage the team | Removes a member. |
| `GET /invitations` | Manage the team | Open invitations: not accepted, revoked or expired. |
| `POST /invitations` | Manage the team | Invites an address with a role, built-in or custom, and a job access (`All` unless it says `HiringTeamOnly`), and emails the link. 404 when no email sender is configured. |
| `DELETE /invitations/{invitationId}` | Manage the team | Revokes an open invitation. |
| `GET /membership-log` | Manage the team | The latest 200 entries of the membership log. |
| `GET /team/history`, `GET /team/last-update` | Manage the team | The Team page's change history and its Last updated, in words (`docs/architecture/change-history.md`). |

`TeamPolicy` (`src/ScreenHire.Domain/Organizations`) says who may give which role, by permissions: an actor with
Manage the team may give, take away, make, change or delete a role only when it holds nothing their own role does not.
For the built-in roles that is the old rule: Owners manage everyone; Admins manage Admins, Recruiters, Viewers and every
custom role but cannot make anyone an Owner, change an Owner's role or remove an Owner. Refusing an Owner-level role is
a 403 "Owners only"; any other refusal is 403 "More access than your role". A role in a request that is not one of the
organization's is 400 on `customRoleId`. Nobody can demote or remove the last Owner (409 "Last owner"). Every change runs under a
per-organization advisory lock, so two Owners demoting each other at once cannot leave the organization with none.
An address that is already a member gets 409 on invite. A team route narrows `organization_members` to the current
organization itself, because the filter and the `own_memberships` policy also show the user's memberships elsewhere.

**Invitations** (`organization_invitations`, tenant-owned with row-level security) work like sign-in links: only the
SHA-256 hash of the token is stored, an invitation expires after `ScreenHire:Auth:Invitation:Lifetime` (7 days), one
conditional UPDATE uses it up, and the link opens the dashboard's `/invitations/accept#invitation=...` page, so the
token stays out of server logs and the person confirms with a button. The page is open whoever is signed in
(`guardRedirect` in `apps/dashboard/src/lib/auth.ts`), because accepting signs in as the invited address. A new invitation to the same address revokes the open
one, under the same advisory lock as role changes, so two at once still leave one open. Replacing is revoking, so an
Admin cannot replace an invitation to become an Owner (403). The token is the organization's id followed by 256 random bits (`apps/api/Auth/Invitations.cs`). Accepting reads
the id, makes that organization the tenant, and looks for the hash inside it. The id only says where to look; the
hash match is what authorizes. Accepting signs in as the user with the invited address, whoever the browser was signed
in as, and creates that user if needed, because following the link proves the address. The membership, the log entry
and the active organization are written in one transaction. The email goes out inside the request after the commit,
like a sign-in link, so the token never sits in the outbox's message table. Inviting counts against the per-IP auth
rate limit.

**The membership log** (`membership_events`) records every invitation, revocation, join by invitation, role change and
removal with who did it, who it was about and the roles before and after, a custom role by its id and its name at the
time; every custom role made, changed (`RoleUpdated`, with the old name after a rename) or deleted; and every job access
change (`JobAccessChanged`) and person added to or removed from a hiring team (`AddedToHiringTeam`,
`RemovedFromHiringTeam`, with the job or the hiring template). An invitation and a join record their job access. A job a
key creates starts with its template's team, and those entries name the key (`actorApiKeyId`) instead of a person. The
log shows job titles and template names as they are now, and leaves out entries about jobs the reader cannot see. Two membership facts have no entry: the
founding Owner, whose membership onboarding creates with the organization, and an invitation accepted by someone who is
already a member, which only uses up the invitation (its row keeps `accepted_at` and `accepted_by_user_id`). It is append-only for audit: `screenhire_app` has no
`UPDATE` or `DELETE` on it. It holds user ids and no foreign key to `organization_members`, so a removal keeps its own
record. The Team page's History sheet reads the change history instead, which says the same team changes in words and
also records the founding Owner, as "created the team". Hiring teams stay in the membership log only.

### Job access

ADR-0015. Each job has a hiring team, `jobs.hiring_team`, the membership ids of the people who work on it. A member's
job access is `All` or `HiringTeamOnly` (`organization_members.job_access`); Owners and Admins see all jobs whatever it
says (`JobAccessRules.SeesAllJobs`). A member on Only jobs they're on sees a job, its applications and everything under
them, only while they are on its team; another job is a 404 like another organization's, and lists, Tasks, views (`saved-views.md`),
search (`GET /v1/search`, the dashboard's Ctrl K, signed-in people only, which starts from applications so a candidate
shows only through a job the reader sees), reports (`reports.md`) and live updates leave it out. Existing members and new invitations start at All jobs.

`apps/api/Endpoints/HiringTeamEndpoints.cs`, session only (a team decides who sees a job's candidates, which API keys do
not change):

| Route | Who | What it does |
|---|---|---|
| `GET /v1/jobs/{jobId}/hiring-team` | Anyone who sees the job | The people on the team, with their role name and whether they see all jobs. |
| `PUT /v1/jobs/{jobId}/hiring-team` | Edit jobs | Sets the team to `memberIds`, plus another job's team when `copyFromJobId` names one the caller sees (400 otherwise). Someone who is not a member is 400. A member on Only jobs they're on cannot take themselves off (400). Raises `HiringTeamChanged`. |
| `GET /v1/templates/{templateId}/hiring-team` | Any member | A hiring template's default team; empty for a ScreenHire template. |
| `PUT /v1/templates/{templateId}/hiring-team` | Edit hiring templates | Sets it. 409 for a ScreenHire template, which has none. |

`POST /v1/jobs` starts a job with its template's default team, current members only, and the person who creates it, in
the same insert. Every person added or removed is a membership log entry, written under the member lock.

The subset rule covers job access (`TeamPolicy.MayGiveJobAccess`): a member on Only jobs they're on cannot give All jobs,
invite with it, or give the Owner or Admin role (403 "Needs all jobs"). Manage API keys, Manage webhooks and Manage
integrations need all jobs (`RolePermissions.NeedsAllJobs`). A mention names only someone who can see the candidate (400 otherwise), and the
mention email is skipped for someone taken off the job since. Adding a candidate by hand to a job is 409 for a member on Only jobs they're on
when that email is already a candidate only on jobs they cannot see.

The dashboard: Team has a Jobs column ("All jobs" or "2 jobs"), and Edit access in a member's menu opens one sheet for
their role and job access, which lists their jobs read only (`member-access-sheet.svelte`). The invite sheet asks for job
access. The job page has a Hiring team button with its count (`hiring-team-sheet.svelte`), with Add a person and Copy
from another job, and a hiring template its Default hiring team. The Role page shows each person's jobs. Settings leaves
out API keys and Webhooks for someone on Only jobs they're on.

## Tenant isolation

Two layers, so one mistake is not a breach.

1. **EF Core named query filter** `Tenant` on every entity that implements `ITenantOwned`, keyed on
   `ITenantContext.OrganizationId`. A context with no organization sees no tenant rows. `OrganizationMember` also
   shows the current user's own memberships, which is how the API finds their organization.
2. **Postgres row-level security**, enabled and forced, on `jobs`, `organization_members` and `organizations`
   (migration `AddSignInAndTenantIsolation`). Policies compare `organization_id` with
   `NULLIF(current_setting('app.tenant_id', true), '')::uuid`. `organizations` is readable by anyone (careers pages
   resolve slugs) and insertable by anyone (onboarding), but only the organization itself may change or delete its
   row, so its columns must stay safe to show publicly. An endpoint queries it by slug or by the caller's own
   organization id, never as a list: no policy stops a list of every tenant. The one list is a worker message that
   walks every organization in turn (`ReportOpenJobs`), reading ids only.

   The hiring tables (migration `AddHiringModel`) follow the `jobs` pattern: `job_versions`, `candidates`,
   `applications` and `application_events` each carry `organization_id` and a `tenant_isolation` policy. Child tables
   reference their parent with a composite foreign key on `(organization_id, id)`, because foreign key checks bypass
   row-level security: an application can only point at a job and a candidate of its own organization, whatever id a
   request carries. `job_versions` and `application_events` are append-only: `screenhire_app` has no `UPDATE` or
   `DELETE` on them. `application_scorecards` (migration `AddScorecards`) is the same: a submitted scorecard is final.

   The verification tables (migration `AddVerificationEngine`) follow the same pattern. `verification_analyzer_runs`,
   `verification_signals`, `verification_assessments` and `verification_review_actions` are append-only for audit;
   `submission_observations` refuses `UPDATE` but allows `DELETE`, which the retention sweep needs. The sweep walks every
   organization in turn, like `ReportOpenJobs`. `signal_network_entries` is the one cross-tenant table: row-level
   security with no policy and no grant to `screenhire_app`, so the application role cannot touch it while the Signal
   Network stays off (ADR-0005).

   The team tables (migration `AddTeamMembers`) follow the `jobs` pattern: `organization_invitations` and
   `membership_events` each have a `tenant_isolation` policy, and `membership_events` is append-only.

   System hiring templates are the one shared data: `hiring_templates` and `hiring_template_versions` rows with a NULL
   `organization_id`. A `system_read` policy lets every tenant read them, the write policies require the caller's own
   organization, so no tenant can change one, and the entities carry a hand-written query filter (own organization or
   system) instead of `ITenantOwned`. Published template versions are immutable in the database too: the update and
   delete policies match drafts only.

   `api_keys` (migration `AddApiKeys`) follows the `jobs` pattern too, without `DELETE` for `screenhire_app`.

   `custom_roles` (migration `AddCustomRoles`) follows the `jobs` pattern. `organization_members.custom_role_id` points at
   it with a composite foreign key on `(organization_id, custom_role_id)`, restricting deletes, so a member can only hold
   a role of their own organization. `organization_invitations.custom_role_id` has no foreign key (ADR-0014): it is only
   resolved under row-level security, when the invitation is written and again when it is accepted, and open
   invitations block the role's deletion instead. Both tables check that `role` is `Custom` exactly when the id is set.

   `change_records` (migration `AddChangeHistory`), the history of templates and jobs, follows the `jobs` pattern and
   is append-only: `screenhire_app` has no `UPDATE` or `DELETE` on it ([change-history.md](change-history.md)).

   Job access (migration `AddJobAccess`, ADR-0015) adds a `job_scope` policy, `AS RESTRICTIVE` and `TO screenhire_app`,
   to `jobs` and every table under a job or an application (since then, `talent_pool_entries` too, in `AddTalentPool`:
   a member on Only jobs they're on sees the saved candidates from their own jobs). Restrictive policies are ANDed with `tenant_isolation`, so
   they only narrow it. `jobs` shows a row when `app.job_scope` is `all` or `app.member_id` is in `hiring_team`; the
   others ask `app_sees_job` or `app_sees_application`, or their parent row. `candidates` and `change_records` keep
   tenant-only policies: a candidate is one row across jobs and is found by email when they apply again, so the API
   filters candidates itself (`JobScope`), and a job's history is read only through the job. `JobAccessTests` checks that
   every table with a `job_id` or `application_id` has the policy, and that the policies alone hide a job.

`users`, `user_logins`, `user_claims`, `user_tokens`, `user_passkeys` and `magic_link_tokens` have no row-level security, because
sign-in finds a user by email before any organization is known. An endpoint that lists people therefore starts from
`OrganizationMembers` (tenant-filtered) and joins to users, never from `Users`.

The API and worker connect as `screenhire_app`, a role the migration creates without table ownership or `BYPASSRLS`.
Migrations run as the owner. In Development the API migrates with the owner's connection string and sets the role's
password from the `screenhire-app` connection string on every start; production does both in its deploy step.

`TenantSessionInterceptor` sets `app.tenant_id`, `app.user_id`, `app.job_scope` and `app.member_id` with
`set_config(..., true)` for every command:
inside a transaction (every SaveChanges, because the context forces one) as its own command first, and outside one
as a `DO` block in front of the query. The setting ends with the transaction, so a pooled connection never carries it
into the next request.

`app.job_scope` is `all` when there is no user (an API key, a worker handler, the careers pages, the seeders) and for a
member who sees all jobs, and `team` otherwise. A user whose membership the request never resolved gets `team` with no
member, and so no jobs: a path that forgets fails closed. The named query filter `JobScope` mirrors the policies.

Who the tenant is:

| Request | Tenant |
|---|---|
| Signed in | `TenantResolutionMiddleware`: the user's active organization (or first membership) and their job access there, read on every request |
| API key, or an OAuth connection on `/v1/mcp` | `TenantResolutionMiddleware`: the key's organization, with no user ("API keys" above), and the key's id, which change history records as the actor. Every job. An OAuth code or token names its organization, as a key does, and the token endpoint and the OAuth handler set it before the lookup. |
| Public careers endpoint | The organization whose slug is in the route, for reads and for the apply flow's anonymous writes, which only add an application (or an upload for one) to that organization. Every job, whoever is signed in (`SetPublicOrganization`). |
| Onboarding | The new organization, from just before it is saved |
| Accepting an invitation | The organization the token names, for the lookup by hash and the join that follows |
| Switching organization | The chosen one, after a check that the user is a member of it |
| Worker handler | `TenantMessageMiddleware`: the `OrganizationId` of an `ITenantMessage`; none for system messages, which set each organization in turn. Every job. |

### Row-level security and indexes

A user's condition on a table with row-level security (the search box's `ILIKE '%text%'`) is applied after the policy, and
Postgres lets a planner use an index for it only when its function is **leakproof**, which `texticlike` and `textlike`
are not. So as `screenhire_app` the trigram indexes on `candidates.full_name`, `candidates.email` and `jobs.title`
(migration `HardenFromWorkflowFollowUps`) do not serve the search today: `EXPLAIN` as that role reads the organization's
rows through an `organization_id` index and applies the `ILIKE` to them as a filter, so a search costs one organization's
rows, not the whole table, while the owner (a superuser, which skips the policy) uses the trigram index. The indexes are right for the query's shape (`SearchTests` proves it as the owner) and cost writes, and they
help the day a leakproof wrapper exists. Marking a function leakproof needs a superuser, so it is an owner action
before anything claims a faster search; check any new index for an RLS table with `EXPLAIN` as the application role,
never the owner. `pg_trgm` is a trusted extension, and Azure Database for PostgreSQL also needs it in the server's
`azure.extensions` list before `CREATE EXTENSION` runs.

### Adding a tenant-owned table

Implement `ITenantOwned` on the entity, which gives it the query filter. In its migration, copy the `jobs` block from
`AddSignInAndTenantIsolation`: enable and force RLS, and add the `tenant_isolation` policy. Grants come from the
default privileges. Add an integration test that another organization's rows are invisible. `/add-migration` has the
checklist.

## Deploy prerequisites

Hosting is Azure Container Apps (ADR-0007), defined in `apps/apphost/AzurePublish.cs` and written up in
[deploy](../operations/deploy.md), which does each of these. Whatever runs the API in production must:

- run the EF migrations and then `ScreenHireMessaging.ProvisionStorageAsync` as the owner, in that order, on every
  release (docs/architecture/messaging.md, "Roles and provisioning"). The API does both at startup in Development.
  Both steps must use the same owner role: the migrations' default privileges grant `screenhire_app` access only to
  tables that role creates later. The migrator also needs `CREATEROLE`, or `screenhire_app` must exist beforehand
  (the migration skips creating it when it does);
- give `screenhire_app` a login and password, which the migration creates as `NOLOGIN` (the Development seed does it
  locally), and give the API and the worker only that role's connection string, `ConnectionStrings:screenhire-app`;
- protect the ASP.NET Core Data Protection key ring at rest. The session, external-sign-in and antiforgery cookies are
  encrypted with it. The ring is kept in Postgres (`data_protection_keys`, `AddScreenHireDataProtection`) and shared
  by every API instance and the worker, which signs follow-up questionnaire links with it (workflow-engine.md §8), so it
  survives restarts; its keys are stored as Data Protection writes them unless
  `ScreenHire:DataProtection:KeyVaultKeyUri` names a Key Vault key, which wraps them
  (`ProtectKeysWithAzureKeyVault`). A production host refuses to start without it;
- list its reverse proxies in `ScreenHire:Auth:TrustedProxies`, as addresses or CIDR networks (several in one entry, separated
  by commas), with `ScreenHire:Auth:ForwardLimit` set to the number of trusted hops;
- set `ScreenHire:ApiKeys:PermitsPerMinute` for the number of API instances, because each one counts a key's requests
  on its own;
- give the worker an SMTP connection (`ConnectionStrings:mailpit` until the production provider is chosen) and
  `ScreenHire:Email:From`, or automations skip their emails (docs/architecture/workflow-engine.md section 8);
- give the API and the worker the same secret `ScreenHire:Webhooks:SigningKey`, or CallWebhook automations fail as
  issues and webhook endpoints cannot be created. The worker signs with it; the API derives each subscription
  endpoint's secret from it to show it once. Never share it: receivers get their organization's derived key
  (`WebhookSignature.OrganizationKey`) or their endpoint's secret (`WebhookSignature.EndpointSecret`),
  docs/architecture/workflow-engine.md sections 5 and 8. Changing it changes every endpoint's secret.

## Local development and tests

The AppHost runs Mailpit (every email lands there; web UI and API on http://localhost:8025 with `pnpm dev`, a random
port in the tests) and,
in Development only, **Dev SSO**: navikt/mock-oauth2-server with the seeded users in `apps/apphost/DevSso/config.json`,
listed on its login page.

| Dev SSO user | Email | Signs in as |
|---|---|---|
| Avery Quinn | `avery@brightwater.example.com` | Owner of Brightwater Studio (seeded) |
| Jordan Ellis | `jordan@brightwater.example.com` | Recruiter at Brightwater Studio (seeded) |
| Robin Hale | `robin@brightwater.example.com` | Viewer at Brightwater Studio (seeded), made an Interviewer on Only jobs they're on, with the Senior .NET Engineer job, by the local demo |
| Sam Rivera | `sam@example.org` | A new user, until they onboard |
| Unverified address | Avery's address, `email_verified: false` | Refused, as an unverified provider would be |
| Any email address | what you type | A new user, or the existing one with that email |

Tests sign in the way people do: `ApiSession` (`tests/ScreenHire.Testing`) drives the magic link through Mailpit or
the Dev SSO flow, and the Playwright journeys use the real pages. There is no test-only sign-in. The passkey integration
tests use `PasskeyAuthenticator` (`tests/ScreenHire.Testing`), a software authenticator that builds the registration and the
signature the way a device does; the end-to-end test uses Chromium's virtual authenticator through the DevTools `WebAuthn`
domain, which needs no prompt and works headless.
