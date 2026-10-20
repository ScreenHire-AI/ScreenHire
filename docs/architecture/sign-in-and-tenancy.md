# Sign-in, organizations and tenant isolation

How a recruiter signs in, how their organization is chosen, and how every tenant-owned query stays inside that
organization. Decisions: ADR-0003 (domains are configuration), ADR-0008 (Identity, cookie auth, named query filters
plus RLS). Non-negotiables 6 and 7 in CLAUDE.md.

## Sign-in

There are no passwords. A person signs in with an identity provider or an emailed link, and the first sign-in creates
their account. ASP.NET Core Identity is the user store (`ApplicationUser`, tables `users`, `user_logins`,
`user_claims`, `user_tokens`); the session is the `sh.session` cookie (HttpOnly, SameSite=Lax, 14 days sliding).

The dashboard calls the API same-origin through its `/v1` path (the Vite proxy locally), so cookies are host-only on
`ScreenHire:Domains:AppHost` and need no CORS. Every route requires a signed-in user unless it calls
`.AllowAnonymous()` (a fallback authorization policy in `apps/api/Auth/AuthenticationSetup.cs`).

| Route | What it does |
|---|---|
| `GET /v1/auth/providers` | The configured providers and whether email links work. Issues the antiforgery token. |
| `GET /v1/auth/me` | The signed-in user and their organization, or 401. Issues the antiforgery token either way. |
| `GET /v1/auth/challenge/{provider}?returnUrl=` | Redirects to the provider (authorization code + PKCE). |
| `GET /v1/auth/signin-{provider}` | The handler's redirect URI. The provider returns here with a code. |
| `GET /v1/auth/callback?returnUrl=` | Links or creates the user, signs them in, redirects to the dashboard. |
| `POST /v1/auth/magic-link` | Emails a sign-in link. Always 202, known address or not. |
| `POST /v1/auth/magic-link/verify` | Redeems a link's token and signs in. |
| `POST /v1/auth/onboarding` | Creates the user's organization (name and slug) with them as Owner. |
| `POST /v1/auth/sign-out` | Ends the session. |

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
provider is a later decision. Sending happens inside the request until Wolverine lands.

### Abuse controls

- **Antiforgery.** Every POST under `/v1/auth` needs the `X-XSRF-TOKEN` header. The API puts the token in the
  readable `XSRF-TOKEN` cookie on `providers` and `me`, and the generated client's `apiFetch` echoes it on every
  non-GET request. Tokens are bound to the user, so sign-in and sign-out re-issue them.
- **Rate limit.** A fixed window per client IP on the challenge and every auth POST, `ScreenHire:Auth:PermitsPerMinute`
  (default 100). Development raises it to 2000 (`apps/api/appsettings.Development.json`): every test signs in
  from 127.0.0.1 against one API, and the integration suite alone sends about 80 such requests in 20 seconds.
  The per-address email cap is unchanged everywhere and has its own test. Behind a reverse proxy the client IP needs
  forwarded-headers configuration.

## Domains

`ScreenHire:Domains` (`apps/api/Configuration/DomainOptions.cs`, validated at start) holds `Scheme`, `BaseDomain`,
`AppHost`, `ApiHost`, `CareersDomain`, `CareersHostPattern` and an optional `CookieDomain`. Development defaults are
in `apps/api/appsettings.Development.json`; the AppHost overrides `AppHost` and `ApiHost` with the real endpoints,
because tests use random ports. `AppHost` drives OIDC redirect URIs, emailed links, redirects after sign-in and the
CORS origin. Leave `CookieDomain` empty: a parent-domain cookie would also reach tenant careers pages.

## Organizations and roles

`OrganizationMember` joins a user to an organization with a role: Owner, Admin, Recruiter or Viewer. The model allows
many organizations per user; the dashboard uses the first one joined. Onboarding creates the organization and the
Owner membership in one transaction. A slug is a DNS label (3 to 63 lower-case letters, digits and hyphens) that is
not reserved, because it becomes a careers subdomain.

## Tenant isolation

Two layers, so one mistake is not a breach.

1. **EF Core named query filter** `Tenant` on every entity that implements `ITenantOwned`, keyed on
   `ITenantContext.OrganizationId`. A context with no organization sees no tenant rows. `OrganizationMember` also
   shows the current user's own memberships, which is how the API finds their organization.
2. **Postgres row-level security**, enabled and forced, on `jobs`, `organization_members` and `organizations`
   (migration `AddSignInAndTenantIsolation`). Policies compare `organization_id` with
   `NULLIF(current_setting('app.tenant_id', true), '')::uuid`. `organizations` is readable by anyone (careers pages
   resolve slugs) and insertable by anyone (onboarding), but only the organization itself may change or delete its
   row, so its columns must stay safe to show publicly.

The API and worker connect as `screenhire_app`, a role the migration creates without table ownership or `BYPASSRLS`.
Migrations run as the owner. In Development the API migrates with the owner's connection string and sets the role's
password from the `screenhire-app` connection string on every start; production does both in its deploy step.

`TenantSessionInterceptor` sets `app.tenant_id` and `app.user_id` with `set_config(..., true)` for every command:
inside a transaction (every SaveChanges, because the context forces one) as its own command first, and outside one
as a `DO` block in front of the query. The setting ends with the transaction, so a pooled connection never carries it
into the next request.

Who the tenant is:

| Request | Tenant |
|---|---|
| Signed in | `TenantResolutionMiddleware`: the user's first membership, read on every request |
| Public careers endpoint | The organization whose slug is in the route |
| Onboarding | The new organization, from just before it is saved |
| Worker | None yet; messages will carry the organization id |

### Adding a tenant-owned table

Implement `ITenantOwned` on the entity, which gives it the query filter. In its migration, copy the `jobs` block from
`AddSignInAndTenantIsolation`: enable and force RLS, and add the `tenant_isolation` policy. Grants come from the
default privileges. Add an integration test that another organization's rows are invisible. `/add-migration` has the
checklist.

## Local development and tests

The AppHost runs Mailpit (every email lands there; web UI and API on a random port shown in the Aspire dashboard) and,
in Development only, **Dev SSO**: navikt/mock-oauth2-server with the seeded users in `apps/apphost/DevSso/config.json`,
listed on its login page.

| Dev SSO user | Email | Signs in as |
|---|---|---|
| Avery Quinn | `avery@brightwater.example.com` | Owner of Brightwater Studio (seeded) |
| Sam Rivera | `sam@example.org` | A new user, until they onboard |
| Unverified address | Avery's address, `email_verified: false` | Refused, as an unverified provider would be |
| Any email address | what you type | A new user, or the existing one with that email |

Tests sign in the way people do: `ApiSession` (`tests/ScreenHire.Testing`) drives the magic link through Mailpit or
the Dev SSO flow, and the Playwright journeys use the real pages. There is no test-only sign-in.
