# ScreenHire Documentation

ScreenHire is a hiring platform (ATS) for SMBs first — growing to mid-market, enterprise and agencies — in the US and
UK/EU. It sets itself apart by **detecting AI-generated, fraudulent and bot applicants with explainable, lawful
evidence**, by **per-job workflows built from user-defined templates**, by a **public API**, and by **in-house
one-click job posting and teardown**.

> Positioning (from research): *an integrity layer from first click to first day that recruiters and candidates can
> both understand, at GoHire prices.*

**Why it exists:** the owner's team hires on gohire.io (mainly .NET developer roles), and fake / AI-generated
candidates get through its screening. **Positioning (owner, 2026-09-23):** a simple, intuitive, affordable tracker for
small hiring teams in the style of GoHire or BambooHR — not an enterprise fraud platform like Sardine or Ashby.
Detection runs in the background (custom models possible later). A public job board alongside the tracker is a
possible later addition. **Domain:** `screenhire.io` planned (not yet bought; `.com` is taken) — the base domain is
configuration everywhere (ADR-0003).

## Decision log

| ADR | Decision | Status |
|---|---|---|
| [0001](adr/0001-api-first-monorepo.md) | API-first, single monorepo | Accepted |
| [0002](adr/0002-dotnet-backend.md) | ASP.NET Core on .NET 10 LTS, PostgreSQL, object storage behind an abstraction | Accepted |
| [0003](adr/0003-astro-public-surfaces.md) | Astro for marketing site (SSG, SEO-first) and careers pages (SSR) | Accepted |
| [0004](adr/0004-evidence-not-verdicts.md) | Integrity output is evidence, not verdicts; never auto-reject on integrity | Accepted |
| [0005](adr/0005-cross-tenant-signal-network.md) | Cross-employer Signal Network: pseudonymised, opt-in, legal-gated | Accepted with gate |
| [0006](adr/0006-dashboard-svelte.md) | Recruiter dashboard: Svelte 5 + SvelteKit 3 SPA | Accepted |
| [0007](adr/0007-hosting-azure.md) | Azure bootstrap (~$45–65/mo pre-credits), Cloudflare in front | Accepted |
| [0008](adr/0008-dotnet-building-blocks.md) | .NET building blocks (Wolverine, Identity, Aspire, Orval, RLS) under licence policy | Accepted |
| [0009](adr/0009-design-system-warm-grid.md) | "Warm Grid" design direction; shadcn-svelte (dashboard) + daisyUI CSS-only (public pages); one token source | Accepted |

## Architecture

- [Integrity Engine](architecture/integrity-engine.md) — signals, two assessments, collection timeline, Signal Network
- [Workflow Engine](architecture/workflow-engine.md) — templates, stages, automations, guardrails, execution model
- [Job Distribution Engine](architecture/job-distribution.md) — adapters, posting state machine, honest teardown

## Design

- [Design System — "Warm Grid"](design/design-system.md) — tokens (light/dark/candidate), type, status semantics,
  components, accessibility, employer branding, voice

## Process

- [Board pipeline](process/board-pipeline.md) — Kangentic columns, outcome lines, notifications and how to unblock a
  task; where the gate, conflict rules and process guard live
- [Testing](process/testing.md) — which tier for what, running one test, the shared Aspire fixture, data isolation,
  end-to-end setup and the flake policy
- [Claude Code tooling](process/dev-tooling.md) - the skills, agents and rules in `.claude/`, and when to use each

## Research (snapshot 2026-10-18)

- [Frontend benchmarks](research/frontend.md)
- [Dashboard: Svelte 5 vs Angular 22](research/dashboard-framework.md)
- [Svelte 5 UI libraries & ecosystem map](research/component-libraries.md)
- [Hosting comparison](research/hosting.md) · [Bootstrap hosting < $250/mo](research/hosting-bootstrap.md)
- [.NET building blocks & licence audit](research/dotnet-building-blocks.md)
- [Legal & regulatory landscape](research/legal.md) — *not legal advice*
- [Job-board integration landscape](research/job-boards.md)
- [Competitive teardown](research/competitors.md)
