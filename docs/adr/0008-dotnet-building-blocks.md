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

## Licence policy
- Prefer MIT / Apache-2.0 / BSD / PostgreSQL licences.
- **CI licence check** (NuGet + npm) fails on unapproved licences; every major-version bump is a licence review.
- Libraries with revenue thresholds (MediatR, MassTransit, Duende, QuestPDF, ImageSharp) are not used unless this ADR is
  amended with the threshold and an exit plan.

## Exceptions

Packages outside the preferred licences, each limited to the scope stated. Anything else needs its own entry here.

| Package | Licence | Scope | Why it is acceptable |
|---|---|---|---|
| `Deque.AxeCore.Commons` (axe-core), via `Deque.AxeCore.Playwright` (MIT) | MPL-2.0 | Test-only: `tests/ScreenHire.E2E` | File-level copyleft that applies only to changes to axe-core's own files, which we do not modify or ship; it never reaches a production artefact. Added 2026-10-20 with the test harness. |
