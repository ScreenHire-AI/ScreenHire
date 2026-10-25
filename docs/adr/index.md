# Decisions

Architecture decision records, oldest first. A changed decision is a new or amended ADR, never a silent edit of an accepted one.

* [ADR-0001: API-first architecture in a single monorepo](0001-api-first-monorepo.md) - Why every client, the dashboard included, reads and writes only through the public HTTP API, and why every app and shared package lives in one monorepo under apps/, packages/ and src/.
* [ADR-0002: ASP.NET Core on .NET 10 LTS for API and workers](0002-dotnet-backend.md) - The backend platform: ASP.NET Core on .NET 10 LTS for the API and worker, PostgreSQL as the system of record with JSONB and pgvector, object storage behind an abstraction, and containers so hosting stays portable.
* [ADR-0003: Astro for all public, SEO-critical surfaces (marketing site + careers pages)](0003-astro-public-surfaces.md) - Why the marketing site is static Astro and the careers pages are Astro SSR: the technical SEO baseline, the 15 KB JS and Lighthouse budgets, tenant careers hosts, the embeddable apply widget as an iframe, and domains as configuration.
* [ADR-0004: Integrity output is evidence, not verdicts](0004-evidence-not-verdicts.md) - The integrity non-negotiables: two categorical assessments with reason codes and counter-evidence, no numeric fraud score, never auto-reject on integrity, two signal categories before a level rises, no emotion or biometric inference, and notice before collection.
* [ADR-0005: Cross-employer Signal Network, pseudonymised, opt-in, legal-gated](0005-cross-tenant-signal-network.md) - The cross-employer Signal Network, accepted with a gate: keyed hashes of hard identifiers only, counts shown at three or more organisations, 90-day retention, opt in to contribute before querying, step-up verification only, and a feature flag that stays off until legal review.
* [ADR-0006: Recruiter dashboard in Svelte 5 + SvelteKit (SPA mode)](0006-dashboard-svelte.md) - Why the recruiter dashboard is Svelte 5 with SvelteKit 3 in SPA mode rather than Angular, the questionnaire components it shares with the Astro apply page through packages/ui, and the libraries picked for the builder, pipeline and tables.
* [ADR-0007: Host on Azure (bootstrap config), Cloudflare in front](0007-hosting-azure.md) - The bootstrap hosting plan, not built yet: Azure Container Apps for the API, careers and worker, a Burstable PostgreSQL Flexible Server, Blob Storage and Key Vault, Cloudflare Free in front with Turnstile, and the monthly cost before credits.
* [ADR-0008: .NET building blocks under a permissive-licence policy](0008-dotnet-building-blocks.md) - The .NET library for each concern (Wolverine, ASP.NET Core Identity, Aspire, OpenAPI with Scalar, Orval, named query filters with RLS, Mapperly) under the permissive-licence policy, and the packages it bans, such as MediatR, AutoMapper and Finbuckle.
* [ADR-0009: "Warm Grid" design direction, shadcn-svelte + daisyUI, one token source](0009-design-system-warm-grid.md) - The Warm Grid look, amended by ADR-0010: self-hosted fonts, shadcn-svelte on bits-ui in the dashboard, daisyUI as CSS only on public pages, one token source for both, and employer brand tokens computed and contrast-checked on the server.
* [ADR-0010: Design system v0.2, simple and approachable](0010-design-system-v0-2.md) - Design system v0.2, which replaces the dense keyboard-first brief: keep it simple first, the 16px root and 14px body, a 1280px layout with page templates, five layers, status first, controls that never wrap, and how times and dates show.
* [ADR-0011: Organization API keys now, OAuth client credentials later](0011-api-keys-for-integrators.md) - Organization API keys on the hiring API only: the shk_ key format, a SHA-256 hash with the key shown once, an authentication scheme beside the cookie, read and write scopes, no integrity levels for a key, and why OAuth client credentials with OpenIddict wait.
* [ADR-0012: Docs as an Open Knowledge Format bundle](0012-docs-open-knowledge-format.md) - Why docs/ is an Open Knowledge Format v0.2 bundle with an index per folder, a routing description and narrow sources per concept, why no file was split, and that Kangentic measured no reading benefit at its size, so the reasons are growth and drift detection.
* [ADR-0013: Design system v0.3, the work area first](0013-design-system-v0-3.md) - Design system v0.3, which amends v0.2: pages that fill the window, status icons instead of shapes, panels with a soft shadow, tabs and a details rail on detail pages, links that go somewhere and buttons that act, tables that sort, counts instead of lists, every fact in one home, and a candidate rating averaged from people's scorecards.
* [ADR-0014: Permissions, not role names, and custom roles](0014-permissions-and-custom-roles.md) - The server checks the permission catalogue on every route instead of role names; organizations make custom roles by copying one; a member holds one role, built-in or custom; nobody gives a role with more access than their own; Owner-only permissions stay with Owners; API keys keep their scopes and are capped by their creator.

## Status

| ADR | Status |
|---|---|
| 0001 to 0004, 0006 to 0008, 0011 to 0014 | Accepted |
| 0005 | Accepted with a gate: the Signal Network stays flagged off until a legal review and DPIA |
| 0009 | Accepted, amended by 0010 |
| 0010 | Accepted, amended by 0013 |
