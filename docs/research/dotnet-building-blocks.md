# .NET Building Blocks — Licence Audit & Recommendations

> Research snapshot: **2026-10-18**. Primary sources (licence files, vendor pages, official docs) unless marked
> **[U]** (unverified). Owner constraint: bootstrapping, predictable costs → prefer MIT/Apache/BSD.

## 1. Licence table

| Library (latest stable) | Licence & change | Commercial threshold | Verdict |
|---|---|---|---|
| MediatR 14.2 | Last Apache: 12.5.0 (2025-04-01). From 13.0 (2025-07-02): RPL-1.5 or paid | Free < $5M revenue **and** ≤ $10M outside capital; then $499–$3,999/yr per library | OK under threshold — **prefer Wolverine handlers or `Mediator` (MIT)** |
| AutoMapper 16.2 | Last MIT: 14.0; paid from 15.0 (2025-07-02) | Same as MediatR | **Avoid** — 14.x has unpatched High DoS (CVE-2026-32933). Use **Mapperly** |
| MassTransit 9.2.3 | Commercial from 9.0.0 (2026-01-06); v8 stays Apache (8.5.11) | Free < $1M revenue; above unpublished (~$4k/$12k/yr [U]); v8 EOS "end of 2026" [U] | OK under threshold — **prefer Wolverine** |
| FluentAssertions 8.x | Xceed non-commercial from 8.0 (2025-01-14); v7 Apache | Any revenue: $129.95/dev/yr ($49.95 small biz) | **Avoid** — use AwesomeAssertions or Shouldly |
| Duende IdentityServer 8.0 | Commercial | Community free < $1M revenue & $3M capital (approval); then from $480/mo | OK under threshold — **prefer OpenIddict** |
| Hangfire 1.8.25 | LGPL-3.0 core + Postgres storage | Pro/Ace $500/$1,500/$4,500/yr | Fine, but **no outbox** |
| ImageSharp 4.1 | Six Labors Split; v4 (2026-05-12) and 3.2.0 (2026-10-09) **fail the build without a key file**; 3.1.12 last without check | Free < $1M (apply for key); then from $799/yr | OK under threshold — **prefer SkiaSharp or NetVips (MIT)** |
| QuestPDF 2026.9 | Last "Community MIT": 2026.5.0; source-available from 2026.6.0; licence v3.0 from 2026-07-06 | Free < $1M; then $1,999 / $4,999 | OK under threshold |
| iText 9.8 | AGPL-3.0 or paid | Any closed-source SaaS | **Avoid** |
| NPOI | Maintenance-fee licence on binaries from 2.8.0 (2026-04-06); 2.7.6 last Apache | ≥ $10k revenue (amount [U]) | Avoid or pin 2.7.6 |
| Finbuckle.MultiTenant 10.1.4 | Apache today; README adds maintenance fee effective 2026-11-10 (README typo "20026") | Any revenue-generating use (amount [U]) | **Avoid** — write tenant resolution ourselves |
| Polly 8.8 | BSD-3; fee on next major after 2026-11-16 | $20/mo at ≥ $20k revenue; only direct references count | Use **via Microsoft.Extensions.Http.Resilience** (exempt) |
| Verify 33+ | Maintenance fee from 2026-09-01 | $5–$100/mo at ≥ $10k revenue | Avoid or pin 32.x |
| **Wolverine 6.51 / Marten 9.50** | MIT (CritterWatch add-on paid, BSL-style [U]) | None | **Use** |
| TickerQ 10.4 | MIT or Apache-2.0 (CLA) | None | Use (fallback) |
| Seq | Proprietary | Individual free (1 user, 50 GB; commercial use [U]); Team $1,090/yr | Dev only |
| OpenIddict, Quartz.NET, FluentValidation, Mapperly, Mapster, Shouldly, AwesomeAssertions, Serilog, OpenTelemetry, Npgsql, EF Core, Scalar, Swashbuckle, NSwag, Kiota, PdfPig, Testcontainers, Respawn, Bogus, ErrorOr, FluentResults, Ardalis.*, DocumentFormat.OpenXml, Pgvector.EntityFrameworkCore, Mediator | MIT / Apache-2.0 / BSD-3 / PostgreSQL — unchanged | None | **Use** |

Outside NuGet: Zitadel → AGPL-3.0 at v3 (2025-03). **MinIO OSS repo archived.** Valkey BSD-3.

## 2. Recommended stack

- **Jobs & outbox — Wolverine** (Postgres + EF Core packages): outbox commits in the same transaction as EF
  `SaveChanges`; durable scheduled messages, retry/backoff, Postgres-backed queues (no broker); cron recurring messages
  since 6.34 (2026-09-07) — e.g., every 5 min fan out one message per board for the job-board sync SLA. Also replaces
  MediatR as in-process mediator. *Fallback:* TickerQ + hand-written outbox (`FOR UPDATE SKIP LOCKED`).
- **Auth now — ASP.NET Core Identity + cookie auth.** .NET 10 passkeys live in `SignInManager`, but only the Blazor
  template wires them; `MapIdentityApi` has no passkey routes and issues proprietary (non-JWT) tokens → write passkey
  endpoints ourselves (Microsoft Learn sample). Add **OpenIddict** only when standard OIDC tokens are needed. Cookie auth
  needs SPA + API on one registrable domain (`app.` / `api.` under the same apex) or an Astro proxy.
- **Auth later — WorkOS** SSO + Directory Sync (SCIM) per enterprise customer: no base fee, no per-user metering, users
  stay in our DB; pass cost into enterprise pricing.

  | Option | 0 SSO customers | 3 SSO+SCIM customers (≈ 2,000 MAU) |
  |---|---|---|
  | **WorkOS** | $0 | $750 ($125 per SSO + per Directory Sync connection; billing period [U]) |
  | Clerk | $25 (free tier lacks passkeys/MFA) | $275 → $425 from 2027-01-01 (SCIM billing) |
  | Auth0 | $0 | $700 (> 5 tenants as Organizations) |
  | Logto | ~$120 | $264 (no SCIM) |
  | Keycloak | $0 + ops | $0 + ops (native SCIM GA only in 26.8.0, 2026-10-01) |

- **Aspire 13.6.1 (MIT)** for local orchestration: Postgres, Valkey, JavaScript hosting (`AddViteApp` /
  `AddJavaScriptApp`) for Astro SSR and the SPA; **SeaweedFS** for local S3 (MinIO integration deprecated).
- **OpenAPI — built-in .NET 10 generator (OpenAPI 3.1) + Scalar.** Set `JsonNumberHandling.Strict` (else integers emit
  as string patterns); avoid polymorphic DTOs (open discriminator bugs).
- **TS client — Orval (MIT)**: stable versioning; Zod schemas + svelte-query client from one spec. Alternatives: Hey API
  (0.x — pin exact), openapi-typescript + openapi-fetch (6 kB, types only). Skip Kiota TS (preview) and NSwag (OAS 3.0).
- **Multi-tenancy — EF Core 10 named query filters + Postgres RLS backstop**, no Finbuckle: `FORCE ROW LEVEL SECURITY`,
  app role that neither owns tables nor bypasses RLS, `set_config('app.tenant_id', …, true)` per transaction. An
  explicit, audited cross-tenant path for the Signal Network (ADR-0005).
- **Resume parsing — PdfPig (Apache) + DocumentFormat.OpenXml (MIT)**; `.doc`/`.rtf` via pinned NPOI 2.7.6 or headless
  LibreOffice. Avoid iText, MuPDF.NET.
- **Near-duplicates — pgvector + Pgvector.EntityFrameworkCore (MIT)** (EF 9/10, HNSW in migrations). Confirm the managed
  Postgres offers `vector`.
- **Observability — OpenTelemetry → Grafana Cloud Free** (10k series, 50 GB logs, 50 GB traces, 14-day retention,
  3 users; Pro $19 + usage). Aspire dashboard for dev. SigNoz cloud ($49/mo) as upgrade path.
- **Everything else:** FluentValidation, Mapperly, ErrorOr, Microsoft.Extensions.Http.Resilience, xUnit v3,
  AwesomeAssertions, Testcontainers, Respawn, Bogus.

## 3. Risks

1. **Licence drift is constant** — ≥ 12 libraries in/near this stack changed terms since Jan 2025; Polly and Finbuckle
   fees start Nov 2026. → **Licence check in CI; every major-version bump is a licence review.**
2. **Wolverine**: single vendor, very fast releases (6.0 on 2026-05-21 → 6.51 by 2026-10-15); recurring messages only
   six weeks old.
3. **Postgres pooling mode**: Wolverine leader election breaks behind transaction-mode PgBouncer; session-level tenant
   settings leak across pooled connections → connect directly or use session mode; use transaction-local `set_config`.
4. **Aspire deployment not stable**: `aspire publish/deploy/do` Preview; JS publish APIs Experimental; only latest
   release supported (13.6 had breaking changes); generated Compose includes the dashboard with unsecured telemetry
   ingest — remove or lock down.
5. **Self-written passkey + cookie auth needs a security review.**
6. Unverified: MassTransit v8 EOS and v9 prices; Finbuckle/NPOI fee amounts; Seq free tier commercial use; CritterWatch
   terms; Hey API registry pricing.

## Sources (accessed 2026-10-18 unless dated)

1. NuGet licence files and publish dates: api.nuget.org/v3-flatcontainer, /v3/registration5-gz-semver2
2. https://www.jimmybogard.com/automapper-and-mediatr-commercial-editions-launch-today/ (2025-07-02) · https://mediatr.io · https://automapper.io
3. https://github.com/advisories/GHSA-rvv3-g6hj-g44x (2026-03-13)
4. https://massient.com · https://masstransit.massient.com/configuration/license/
5. https://xceed.com/products/unit-testing/fluent-assertions/
6. https://duendesoftware.com/pricing · https://duendesoftware.com/products/communityedition
7. https://www.hangfire.io/pricing/
8. https://sixlabors.com/posts/licence-enforcement-changes/ (2025-09-04) · https://sixlabors.com/pricing
9. https://www.questpdf.com/pricing
10. https://www.thepollyproject.org/2026/07/14/polly-osmf-announcement.html
11. https://github.com/orgs/VerifyTests/discussions/1731
12. https://github.com/Finbuckle/Finbuckle.MultiTenant (README 2026-10-07)
13. https://zitadel.com/blog/apache-to-agpl (2025-03-13)
14. https://datalust.co/pricing
15. https://wolverinefx.net/guide/durability/postgresql · https://github.com/JasperFx/wolverine/blob/main/docs/guide/messaging/recurring.md
16. https://github.com/Arcenox-co/TickerQ
17. https://learn.microsoft.com/aspnet/core/security/authentication/passkeys/?view=aspnetcore-10.0 (2026-09-01) · https://learn.microsoft.com/aspnet/core/security/authentication/identity-api-authorization?view=aspnetcore-10.0 (2026-10-07)
18. https://workos.com/pricing · https://clerk.com/pricing · https://auth0.com/pricing · https://logto.io/pricing
19. https://www.keycloak.org/2026/10/keycloak-2680-released (2026-10-01)
20. https://aspire.dev/whats-new/aspire-13 · https://aspire.dev/support (2026-10-07) · https://aspire.dev/integrations/frameworks/javascript · https://aspire.dev/deployment/docker-compose · https://aspire.dev/reference/cli/commands/aspire-deploy
21. https://learn.microsoft.com/aspnet/core/fundamentals/openapi/aspnetcore-openapi (2026-09-04) · https://github.com/dotnet/aspnetcore/issues/57982
22. https://orval.dev · npm (orval 8.43.0, 2026-10-14) · https://github.com/openapi-ts/openapi-typescript
23. https://learn.microsoft.com/ef/core/querying/filters · https://www.postgresql.org/docs/current/ddl-rowsecurity.html
24. https://github.com/pgvector/pgvector-dotnet
25. https://grafana.com/pricing · https://signoz.io/pricing
26. GitHub API: minio/minio (archived), valkey-io/valkey (BSD-3)
