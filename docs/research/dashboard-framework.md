---
type: Research
title: "Recruiter Dashboard: Svelte 5 vs Angular 22"
description: "The scored comparison behind ADR-0006: Svelte 5 with SvelteKit against Angular 22 on speed, measured fit with the Astro candidate pages, component libraries and risks."
tags: [research, svelte, angular]
---
# Recruiter Dashboard: Svelte 5 vs Angular 22

> Research snapshot: **2026-10-18**. Owner ask: "take a really hard look… the faster option that will work best with the
> overall ecosystem". npm data = registry + downloads API for week 2026-10-10..16. "Measured" = probe builds
> (Astro 7.4.0, Svelte 5.57.2, Angular 22.2.3 + @analogjs/astro-angular 2.8.1) run in headless Chrome.
> krausest numbers are from Svelte 5.42 → directional.

**Research recommendation: Svelte 5.57 + SvelteKit 3.1 in SPA mode.** Angular wins tables, forms, PDF and router
maturity; Svelte wins the two things the owner asked for — speed and fit with the Astro candidate surface.

## Scored matrix

| # | Need | Svelte | Angular | Winner — why |
|---|---|---|---|---|
| 1 | Workflow builder | @xyflow/svelte 1.7.0 (2026-09-24), 167k/wk, runes-native | @foblex/flow 19.3.0 (36.6k/wk); ngx-vflow 2.7.0 (8.3k/wk) | **Svelte** — React Flow team (38.7k★); keyboard + aria-live in source; 2.0 tagged `next` |
| 2 | Kanban | svelte-dnd-action 0.9.80 (155k/wk, keyboard built in); @dnd-kit/svelte 0.5.1 (official, pre-1.0) | CDK drag-drop | **Tie** — CDK lacks keyboard dragging and virtual-scroll drag. WCAG 2.5.7 needs a non-drag "Move to stage" control either way |
| 3 | Tables 10k+ rows | TanStack Table 9.3.1 (runes); AG Grid via vanilla API | ag-grid-angular 36.2.0, 271k/wk, official | **Angular** — no maintained Svelte AG Grid wrapper; @tanstack/svelte-virtual still on stores |
| 4 | Questionnaire preview = candidate's own components | One shared `.svelte` component set | Duplicate components, or iframe the real page | **Svelte — decisive** |
| 5 | Forms & schemas | Superforms 3.0.0 (Zod 4 / Valibot) | Signal Forms + `validateStandardSchema` (stable v22) | **Angular** — `shadcn-svelte add form` currently fails on Kit 3. Hey API zod/valibot plugins generate schemas from OpenAPI for either |
| 6 | Rich text | Tiptap 3.32 + svelte-tiptap 3.0.2 | ngx-tiptap 15.0.0 | Tie — same core |
| 7 | Charts / PDF / calendar | LayerChart 2.8.1 (185k/wk); pdf.js or EmbedPDF; @event-calendar/core 5.17 | ngx-echarts 22; ngx-extended-pdf-viewer 31 (165k/wk); FullCalendar 7.1 | **Angular, slightly** — turnkey PDF viewer |
| 8 | i18n / a11y / dark mode | Paraglide 2.29 (also runs in Astro); compiler a11y warnings | @angular/localize; CDK + @angular/aria (stable v22) | Tie — Svelte: one i18n catalog shared with Astro; Angular: first-party a11y primitives |
| 9 | SignalR | @microsoft/signalr 10.0.11 in a `.svelte.ts` class | Same client in a service → signals | Tie |
| 10 | Auth & routing | SvelteKit 3 SPA mode | Angular Router | **Angular on maturity**. Cookie-based BFF makes auth framework-neutral (RFC 10017: "strongly recommended for business applications") |
| 11 | Testing & AI codegen | Vitest browser mode; official Svelte MCP server + autofixer, Claude Code plugin, llms.txt | Vitest default; CLI MCP server; `ng new --ai-config claude-code` | Tie — Angular's edge: owner can review output |
| + | Performance | GM 1.18, 9.7 KB br | GM 1.55, 34 KB br | **Svelte** |
| + | Dev loop | HMR applies script/template/style edits without reload (edited component's own state resets; app state survives) | Component TS edit → full reload (#66170 closed "not planned") | **Svelte** |

**Tally:** Svelte 4 (incl. decisive #4), Angular 4, ties 5.

### LLM codegen drift
- SvelteBench: strong models (pass@1 ≥ 0.9) emit Svelte 4 patterns in 0.4% of samples; weaker models 15–45% (prompts ask
  for runes explicitly, so this understates drift).
- Bigger new risk: **SvelteKit 3 renamed/removed APIs that dominate training data** (`$lib` → `#lib`; `$app/stores` and
  `svelte.config.js` gone).
- Angular has the same issue (NgModules, `*ngIf`), countered by an official best-practices file.

## Ecosystem fit (measured)

Same questionnaire as an Astro island:

| Island | JS (br) | Hydration | Text typed before hydration |
|---|---|---|---|
| Svelte | **18.8 KB** | In place | DOM value + node kept |
| Angular via Analog (default) | 36.6 KB | Replaces server DOM | **Lost** — nodes swapped, telemetry listeners would detach |
| Angular via Analog, `useAngularHydration` | 49.9 KB | In place | Kept |

Analog also forces Angular's TS 6.0 pin onto the Astro site, has ~1.5k downloads/week and effectively one maintainer;
Astro 7 support confirmed only on 7.1.6 (probe on 7.4 passed).

**What sharing buys:** each question type implemented once; the preview *is* what candidates see; candidate pages carry
about half the JS. Angular's fallback (iframe the real Astro route + postMessage drafts) keeps accuracy but loses
click-to-edit in the preview and needs a draft-serving endpoint.

**Design tokens:** tie — Tailwind 4 `@theme` is plain CSS.

**Router:** SvelteKit 3 (adapter-static, `fallback: '200.html'`, `ssr = false`) over svelte-spa-router (hash-based).
Kit 3 shipped 2026-10-01 (~50 breaking changes, codemod), 3.1 on 10-14; 2.x last patched 08-18. New project = nothing to
migrate; the risk is libraries lagging (shadcn-svelte #2915, #2917).

## Component libraries

- **Svelte:** bits-ui (805k/wk) + shadcn-svelte (134k/wk) — runes-native, single publisher. Skeleton 5 (35k/wk) — on Zag,
  4 maintainers. Avoid: legacy Melt UI, svelte-ux 1.x (no runes), flowbite-svelte (no release since 04-07).
- **Angular:** Material + CDK + @angular/aria (strongest a11y). Spartan 1.8 (young, one maintainer). **PrimeNG 22 no
  longer MIT except for very small organisations.** Taiga, ng-zorro: weak a11y docs.
- Neither side has a WCAG 2.2 audit — 2.5.7 (dragging), 2.5.8 (target size), 2.4.11 (focus not obscured) are on us.

**Svelte shortlist: (1) shadcn-svelte + bits-ui, (2) Skeleton 5 — both work in Astro islands.**
- bits-ui Select measured in an island: portal + keyboard work, ≈ +32 KB br.
- Svelte context doesn't cross islands → wrap compound components in one `.svelte` file.
- Avoid shadcn's Form component (SvelteKit-only). Keep candidate-facing questions on **native controls**.
- bits-ui scroll-lock breaks a strict CSP (open issue).

## Risks & mitigations

**Svelte**
1. *Owner can't review Svelte yet* → `compilerOptions.runes: true` (verified: `export let` / `$:` fail to compile;
   `on:` / `<slot>` warn); `svelte-check --fail-on-warnings`; Svelte MCP autofixer; Vitest browser tests (a runes
   component compiled cleanly but threw `props_invalid_value` at runtime — only the browser test caught it).
2. *Kit 3 is new, libraries lag* → pin 3.1.x; Kit 3 rules in CLAUDE.md; shadcn `field` + plain Zod.
3. *Thinner enterprise libs* → wrap AG Grid vanilla API once (or TanStack Table + virtualizer); pdf.js; expect a Svelte
   Flow 2.0 migration.
4. *Hiring pool / funding* → SO 2025 usage Svelte 7.2% vs Angular 18.2%; State of JS 2025 retention 86% vs 48%; Svelte 5
   production verified at Brave Search; Vercel employs core maintainers; Open Collective ≈ $20k/yr.

**Angular**
1. Poor Astro fit → Angular never ships to candidate pages; questions built twice.
2. Slower dev loop.
3. Cadence low-risk: yearly majors (v23 ≈ June 2027), 24-month support.

Choose Angular only if the guardrails above won't be adopted, or AG Grid Enterprise-level features dominate the product.

## Sources

1. https://registry.npmjs.org · https://api.npmjs.org (2026-10-18)
2. https://svelte.dev/blog/sveltekit-3-is-here (2026-10-01) · https://svelte.dev/docs/kit/migrating-to-sveltekit-3 · github.com/sveltejs/kit CHANGELOG 3.0.0
3. https://angular.dev/reference/releases
4. https://angular.dev/api/forms/signals/validateStandardSchema · https://blog.ninja-squad.com/2026/06/03/what-is-new-angular-22.0
5. https://github.com/angular/angular/issues/66170 (closed 2025-12-19)
6. https://github.com/angular/components/issues/25468 · https://github.com/angular/components/issues/10122
7. https://www.rfc-editor.org/rfc/rfc10017 (Aug 2026)
8. https://github.com/analogjs/analog/issues/2454 (2026-07-31)
9. https://svelte.dev/docs/mcp/tools · https://svelte.dev/docs/ai/claude-plugin · https://svelte.dev/llms.txt
10. https://angular.dev/ai/mcp · https://angular.dev/ai/develop-with-ai
11. https://survey.stackoverflow.co/2025/technology
12. https://2025.stateofjs.com/en-US/libraries/front-end-frameworks/
13. https://github.com/khromov/svelte-bench (run 2026-10-17)
14. https://github.com/huntabyte/shadcn-svelte/issues/2915 · …/2917
15. https://primeui.dev/licenses/community (2026-08-26)
16. github.com/hey-api/openapi-ts docs
17. https://gitnation.com/contents/how-i-taught-llms-how-to-svelte (2026-06-11)

**Unverified:** large Svelte 5 B2B dashboards in production; Foblex Flow a11y; measured effect of MCP servers on output
quality; Angular MCP stability; Analog on Astro 7.4 beyond the probe.
