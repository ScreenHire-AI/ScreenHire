---
type: Decision
title: "ADR-0006: Recruiter dashboard in Svelte 5 + SvelteKit (SPA mode)"
description: "Why the recruiter dashboard is Svelte 5 with SvelteKit 3 in SPA mode rather than Angular, the questionnaire components it shares with the Astro apply page through packages/ui, and the libraries picked for the builder, pipeline and tables."
tags: [adr, dashboard, svelte]
---
# ADR-0006: Recruiter dashboard in Svelte 5 + SvelteKit (SPA mode)

- **Status:** Accepted (2026-10-18) — owner approved together with the component libraries in ADR-0009
- **Research:** `docs/research/dashboard-framework.md`, `docs/research/frontend.md`

## Context
The owner knows Angular well but asked for "the faster option that will work best with the overall ecosystem". The
deep-dive scored 4–4 on features (Angular: tables, forms, PDF, router maturity; Svelte: workflow graph, shared
components, performance, dev loop), with Svelte winning the two stated priorities.

## Decision
- **Svelte 5 (runes) + SvelteKit 3.1.x in SPA mode** (`adapter-static`, `fallback: '200.html'`, `ssr = false`) for
  `apps/dashboard`, served from a CDN.
- **Shared Svelte components** between Astro islands (candidate questionnaire) and the dashboard's questionnaire-builder
  preview, in `packages/ui` — the decisive ecosystem advantage (measured: Svelte island 18.8 KB br vs 36.6–49.9 KB for
  Angular via Analog, and Svelte preserves pre-hydration input that telemetry listeners depend on).
- Key libraries (to confirm in ADR-0009): @xyflow/svelte (workflow builder), svelte-dnd-action (kanban, plus a non-drag
  "Move to stage" control for WCAG 2.5.7), TanStack Table + virtualizer (or AG Grid vanilla API wrapped once), Tiptap,
  LayerChart, Paraglide (i18n shared with Astro), @microsoft/signalr, TanStack Query (via Orval-generated client).

## Guardrails (because the owner can't yet review Svelte fluently)
- `compilerOptions.runes: true` (legacy `export let` / `$:` fail to compile).
- `svelte-check --fail-on-warnings` in CI; Vitest **browser-mode** component tests (catch runtime-only rune errors).
  - *Deviation, 2026-10-20 (test harness):* component tests run on **jsdom** with @testing-library/svelte, as that
    task's brief specified. They run the real Svelte 5 runtime, and the Playwright end-to-end tests render the
    dashboard in Chromium, so runtime-only rune errors are still caught. Browser mode stays the target. Switch to it
    when a component depends on real layout or browser APIs that jsdom lacks. It would bring npm Playwright's own
    Chromium, separate from the one `pnpm e2e:install` provides.
- Svelte MCP server + autofixer for AI-assisted development; SvelteKit 3 rules (`#lib`, no `$app/stores`, no
  `svelte.config.js`) written into `CLAUDE.md`.
- Pin SvelteKit 3.1.x; candidate-facing questions use native controls.

## Consequences
- Faster dashboard (krausest GM 1.18 vs 1.55; 9.7 vs 34 KB br) and state-preserving HMR.
- Thinner enterprise component ecosystem than Angular; some wrappers we write ourselves.
- Smaller hiring pool (SO 2025: 7.2% vs 18.2%) but much higher developer retention (State of JS 2025: 86% vs 48%).
