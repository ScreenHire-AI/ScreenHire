# ADR-0008: .NET building blocks under a permissive-licence policy

- **Status:** Accepted (2026-10-18) — technical defaults, revisitable per component
- **Research:** `docs/research/dotnet-building-blocks.md`

## Decision

| Concern | Choice | Notes |
|---|---|---|
| Messaging, durable jobs, outbox, scheduling, in-process mediator | **Wolverine** (MIT) + Postgres/EF Core | Outbox in the same transaction as `SaveChanges`; cron recurring messages for board sync. Fallback: TickerQ + hand-written outbox |
| Auth (now) | **ASP.NET Core Identity, cookie auth**, passkeys via `SignInManager` (own endpoints) | SPA + API under one apex (`app.` / `api.`). OpenIddict when standard OIDC tokens are needed |
| Auth (enterprise) | **WorkOS** SSO + Directory Sync (SCIM), per-connection pricing | Users stay in our DB |
| Local orchestration | **Aspire 13.6** (MIT): Postgres, Valkey, Azurite, Astro + Vite apps | Remove/secure dashboard in any generated Compose output |
| API contract | Built-in **.NET 10 OpenAPI 3.1** + **Scalar** | `JsonNumberHandling.Strict`; avoid polymorphic DTOs |
| TS client | **Orval** (MIT) → Zod schemas + svelte-query client | Generated in CI |
| Multi-tenancy | **EF Core 10 named query filters + Postgres RLS backstop** | No Finbuckle (fee from 2026-11-10). Explicit audited cross-tenant path for the Signal Network |
| Validation / mapping / results | FluentValidation, **Mapperly**, ErrorOr | No AutoMapper (commercial + unpatched CVE) |
| HTTP resilience | Microsoft.Extensions.Http.Resilience | Not Polly directly (fee on next major) |
| Resume parsing | PdfPig + DocumentFormat.OpenXml; `.doc`/`.rtf` via LibreOffice headless or NPOI 2.7.6 pinned | No iText, no MuPDF.NET |
| Near-duplicates | pgvector + Pgvector.EntityFrameworkCore | HNSW in migrations |
| Images | SkiaSharp or NetVips | Not ImageSharp (build-time key enforcement) |
| Observability | OpenTelemetry → Grafana Cloud Free | Aspire dashboard in dev |
| Tests | xUnit v3, AwesomeAssertions, Aspire.Hosting.Testing, **Microsoft.Playwright** (MIT) for end-to-end, Deque.AxeCore.Playwright for accessibility; Testcontainers, Respawn, Bogus when needed | No FluentAssertions 8, no Verify 33+. Tests isolate data per organization rather than resetting (docs/process/testing.md) |

## Notes from implementation

- **Sign-in (2026-10-20).** Identity with cookie auth landed as decided, with OpenID Connect handlers for Google,
  Microsoft and generic providers, and emailed links in a hashed, single-use token table (Identity's token providers
  are stateless). Locally, navikt/mock-oauth2-server (MIT) is "Dev SSO" and Mailpit (MIT, through
  CommunityToolkit.Aspire.Hosting.MailPit) catches email; MailKit (MIT) is the SMTP adapter. Soluto's
  oidc-server-mock was rejected: it now builds on Duende IdentityServer. Details:
  `docs/architecture/sign-in-and-tenancy.md`.
- **Passkeys deferred.** .NET 10 has passkey support in `SignInManager`, but only the Blazor template wires the
  WebAuthn endpoints and UI. Adding them means Identity schema version 3 (a `user_passkeys` table), our own
  attestation and assertion endpoints, and dashboard WebAuthn calls. That is its own task; nothing in the current
  design blocks it.
- **Multi-tenancy (2026-10-20).** The named query filter is `Tenant`, on every `ITenantOwned` entity. RLS settings
  are set per command by an EF command interceptor rather than once per transaction, because EF runs plain queries
  without a transaction; SaveChanges always runs in one so the setting can go first as its own command.

## Licence policy
- Prefer MIT / Apache-2.0 / BSD / PostgreSQL licences.
- **CI licence check** (NuGet + npm) fails on unapproved licences; every major-version bump is a licence review.
- Libraries with revenue thresholds (MediatR, MassTransit, Duende, QuestPDF, ImageSharp) are not used unless this ADR is
  amended with the threshold and an exit plan.

## Exceptions

Packages outside the preferred licences, each limited to the scope stated. Anything else needs its own entry here.

| Package | Licence | Scope | Why it is acceptable |
|---|---|---|---|
| `Deque.AxeCore.Commons` (axe-core), via `Deque.AxeCore.Playwright` (MIT) | MPL-2.0 | Test-only: `tests/ScreenHire.E2E` | The whole package is MPL-2.0 (Deque's .NET code and the bundled axe-core). Its copyleft is file-level: it applies only to modified versions of those files, which we neither modify nor ship; it never reaches a production artefact. Added 2026-10-20 with the test harness. |
| `axe-core` (npm), via `lighthouse` (Apache-2.0) | MPL-2.0 | Dev-only: `apps/marketing/scripts/lighthouse.mjs` (`pnpm verify:lighthouse`) | Lighthouse bundles axe-core for its accessibility audits. The copyleft is file-level and covers only modified versions of those files, which we neither modify nor ship; `lighthouse` is a devDependency and never reaches the built site. Added 2026-10-20 with the marketing budgets. |
- **Workflow engine and transactional email (2026-10-20).** Email templates use Fluid.Core (MIT, Liquid syntax; its
  Parlot and TimeZoneConverter dependencies are MIT too). Scriban (BSD-2-Clause) would also pass the policy; Fluid was
  chosen for explicit HTML encoding per render, which keeps candidate names out of the markup. The CallWebhook action
  uses one long-lived `HttpClient`, not `IHttpClientFactory`: the service defaults add a retrying resilience handler to
  every factory client, Wolverine already retries the action, and the opt-out (`RemoveAllResilienceHandlers`) is still
  experimental. Details: `docs/architecture/workflow-engine.md` section 8.
