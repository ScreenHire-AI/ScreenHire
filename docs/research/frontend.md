---
type: Research
title: "Frontend Framework Benchmarks & Stack Options"
description: "Benchmarks and stack options for the two frontends, the SEO-critical candidate surface and the recruiter dashboard: Astro, Blazor, Angular and Svelte compared on size and speed, behind ADR-0003."
tags: [research, frontend, benchmarks]
---
# Frontend Framework Benchmarks & Stack Options

> Research snapshot: **2026-10-18**. Constraint from the owner: highly fluent in C#/.NET and Angular, open to Blazor;
> wants "Astro-level" speed — performance above all — but with good DX (HMR, tooling).

## Two surfaces, different needs

1. **Candidate surface** — public careers/job pages, apply form, questionnaire. SEO-indexed, must load instantly on
   mobile, embeds the behavioral-telemetry module. Also an embeddable widget for customer sites.
2. **Recruiter dashboard** — authenticated SPA: drag-and-drop workflow builder (node graph), kanban pipelines, large
   data tables, candidate-analysis views.

## Benchmark table

- **Source:** krausest js-framework-benchmark, Chrome 154, published **2026-10-04**, M4 MacBook Pro (desktop numbers;
  mobile CPUs are several times slower) [1].
- **GM:** weighted geometric mean over the 9 CPU benchmarks; 1.00 = fastest of 196 keyed implementations (rank).
- **Mobile CWV pass:** share of public origins passing all three Core Web Vitals on mobile, HTTP Archive Sept 2026 [2]
  — public pages only, correlational.

| Framework (bench version) | GM (rank) | JS Brotli KB | Ready mem MB | First paint ms | Mobile CWV pass |
|---|---|---|---|---|---|
| Vanilla JS | 1.05 (#7) | 2.5 | 0.48 | 54 | – |
| Vue Vapor 3.6 rc.5 | 1.11 (#28) | 18.7 | 0.72 | 87 | – |
| Solid 1.9.3 | 1.13 (#42) | 4.5 | 0.60 | 52 | – |
| Svelte 5.42 | 1.18 (#55) | 9.7 | 0.67 | 71 | 53.6% |
| Lit 3.2 | 1.30 (#83) | 7.3 | 0.72 | 69 | – |
| Vue 3.5.39 | 1.31 (#88) | 23.3 | 0.78 | 102 | 36.2% |
| Preact 11 (signals / hooks) | 1.43 / 1.65 | 8.3 / 5.9 | 0.69 / 0.64 | 60 / 67 | – |
| **Angular 22.2 zoneless** | **1.55–1.58 (#129)** | 34 | 1.1 | 136–143 | 16.5% |
| Angular 22.2 + zone.js | 1.59–1.68 | 46 | 1.6 | 158–163 | (same) |
| React 19.2 hooks | 1.59 (#140) | 51.4 | 1.19 | 227 | 43.9% |
| Astro 6.3 + React island | 1.63 | 55.1 | 1.23 | **46** | **71.1% (Astro)** |
| Qwik 1.11 | 2.95 (#192) | 30.6 | 0.57 | 49 | – |
| Blazor WASM 10 / AOT | 4.84 / 4.89 (#195/196) | 1,377 / 2,952 | 41 / 52 | 78* / 70* | 26.4% |

Notes:
- krausest measures DOM-update throughput (relevant to the dashboard); no time-to-interactive metric in this run.
- *Blazor first paint is the static "Loading…" shell, not cold start.
- Astro's own entry scores GM 1.06 using manual DOM manipulation, so it isn't used as Astro's speed. The meaningful
  signal: Astro + React paints at 46 ms vs 227 ms for client-rendered React, and Astro sites have the best mobile CWV
  pass rate (71.1%).

## Stack options evaluated

### (a) Astro (candidate) + Angular SPA (dashboard) + ASP.NET Core API — *research recommendation*
- **Astro 7.4.0** (2026-10-15): zero JS by default; official React/Preact/Svelte/Vue/Solid/Alpine integrations; Lit
  integration deprecated since Astro 5 [14][16]. **Actions accept plain HTML forms with no JS** → apply form can be
  HTML + a framework-free TS telemetry module; add one small Svelte/Preact island only if the questionnaire needs rich
  logic.
- **Cost:** per-tenant pages that change constantly need on-demand rendering → a **Node runtime in production**
  (`@astrojs/node` 11.1), or a rebuild per job post. Astro 7 stable route caching helps. Shared DTOs are replaced by a TS
  client generated from OpenAPI (NSwag/Kiota).
- **Ownership:** Cloudflare acquired Astro's company 2026-01-16; MIT license and "deploy anywhere" are stated
  commitments, not contracts [15].
- **Avoid `@analogjs/astro-angular` for candidate pages** (~1.5k downloads/week; each island is a separate zoneless
  Angular app; only `client:load`/`client:visible` documented).
- **Angular 22.2.3** (v22 released 2026-06-03) [11][12]: zoneless default since v21; Signal Forms and `httpResource`
  stable in v22; incremental hydration on by default in v22; template/style HMR works, **TS edits trigger a full reload**
  [18]; Vitest default test runner.
- **Graph libraries (Angular)** [20]: Foblex Flow 19.3 (36.6k/week, 540★, released 2026-09-27); ngx-vflow 2.7
  (8.3k/week, 487★). Both active, small teams.

### (b) All-Blazor (.NET 10 LTS; .NET 11 at RC2, GA reportedly 2026-11-10 [10])
- **Strengths:** shared C# DTOs, one language, one host. Static SSR is plain HTML; `blazor.web.js` (46.5 KB br on .NET
  10, 56.5 KB on .NET 11 RC2) loads only if you opt into enhanced navigation/forms [8] → candidate pages roughly tie
  Astro on payload.
- **Why it fails for this product:** any interactivity costs a WASM download (runtime alone ~1.18 MB br before app
  assemblies; WASM stays on Mono in .NET 11 [9]) or a **server round-trip per event** (Microsoft targets ≤250 ms,
  ~250 KB/circuit, sticky sessions [5]) — wrong for a mobile form that instruments keystrokes. Dashboard on WASM ranks
  last of 196 with 41 MB baseline memory. Z.Blazor.Diagrams: one 2026 release (3.0.4.1, March), 127 open issues.
  QuickGrid is basic (Microsoft points to third-party grids [21]). Hot reload restarts on "rude edits"; several
  hot-reload bugs open on .NET 9–11 [6].

### (c) Angular SSR / Analog for both surfaces
- One framework, but every candidate page hydrates **64–85 KB br** of JS even with incremental hydration (probe build of
  v22.2.3). Deferring the form removes only ~9 KB from the critical path. **No zero-JS mode → can't reach Astro-level
  performance.** Analog 2.8.1 (~17.8k/week, 3.2k★, 3.0 alpha underway → expect churn) [13].

### (d) Astro + Svelte 5 or Solid for both surfaces
- **Svelte:** fastest viable option. **Svelte Flow** from the xyflow team (167k/week, v1.7 2026-09-24). Tiny widgets,
  Vite state-preserving HMR. A dashboard SPA doesn't need SvelteKit (SvelteKit 3 shipped 2026-10-01).
- **Solid:** 2.0 still RC (rc.15); `solid-dnd` stale (2023); no mature flow library (`@dschz/solid-flow` 1.6k/week).
  Not recommended for the dashboard.

### (e) Other combinations
- **Blazor static SSR (candidate) + Angular SPA + API** — single .NET host, shared DTOs, ~0 KB framework JS on public
  pages. **Strongest runner-up if a Node runtime is unwelcome.** Loses Astro's island ergonomics, Vite DX and image
  tooling.
- **Astro + React with xyflow** — `@xyflow/react` 8.35M downloads/week, 38.7k★, ~2 orders of magnitude ahead of
  everything else. Fallback if the workflow builder becomes the product's centrepiece.

## Dashboard: Angular vs Svelte

Speed favours Svelte: GM 1.18 vs 1.55; 9.7 KB vs 34 KB; better graph-library adoption; better HMR. Absolute numbers on
the M4: create 1k rows 22 ms (Svelte) vs 33 ms (Angular); create 10k rows 232 ms vs 302 ms. A recruiter on a desktop
with virtualised tables won't notice; learning a new framework costs weeks. **If raw speed truly outranks fluency,
switch the dashboard to Vite + Svelte 5 + Svelte Flow.** Either way, spike the workflow builder early.

## Other specifics

- **Embeddable widget: iframe, not Shadow DOM.** Serve the apply form + telemetry from our own origin. Shadow DOM does
  not stop the host page forging/reading paste, keystroke and focus events, or reading candidate PII. A custom element
  (Preact or Lit, ~5–6 KB gzip) only for a job-list launcher. Plan for iframe auto-sizing and third-party-cookie limits.
- **Dev loop:** Vite 8 (Rolldown) is best [17]. Angular CLI is good (embeds Vite 8; probe cold dev build 1.1 s).
  `dotnet watch` is the weakest. No credible head-to-head HMR latency numbers found.

## Could not verify

Default Blazor WASM template total size / cold start (1.18 MB runtime floor measured from NuGet packs, excludes app
assemblies); .NET 11 GA date (trade press only); Angular Elements overhead; astro-angular per-island cost; measured CWV
for Angular incremental hydration; Angular bundle sizes come from a throwaway probe build, not a published source; GM
values recomputed from raw data (small rounding differences possible); Angular blog read via a mirror (403).

## Sources (age as of 2026-10-18)

1. https://krausest.github.io/js-framework-benchmark/2026/chrome154.html (2026-10-04)
2. https://cdn.httparchive.org/v1/cwv?technology=Astro,Svelte,React,Vue.js,Blazor,Angular&geo=ALL&rank=ALL&start=latest (Sept 2026 data)
3. https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0 (2026-10-08)
4. https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-11 (2026-10-08)
5. https://learn.microsoft.com/en-us/aspnet/core/blazor/host-and-deploy/server/ (2026-10-08)
6. https://learn.microsoft.com/en-us/aspnet/core/test/hot-reload (2026-09-16)
7. https://learn.microsoft.com/en-us/aspnet/core/blazor/webassembly-build-tools-and-aot (2026-10-13)
8. https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/navigation (2026-09-15)
9. https://devblogs.microsoft.com/dotnet/dotnet-maui-moves-to-coreclr-in-dotnet-11/ (2026-05-13)
10. https://visualstudiomagazine.com/articles/2026/09/14/net-11-rc1-gets-go-live-support-ahead-of-november-launch.aspx (2026-09-14; not fetched directly)
11. https://blog.angular.dev/announcing-angular-v22-c52bb83a4664 (2026-06-03) · https://blog.angular.dev/announcing-angular-v21-57946c34f14b (2025-11-19) · https://blog.angular.dev/announcing-angular-v20-b5c9c06cf301 (2025-05-28)
12. https://angular.dev/guide/incremental-hydration · https://github.com/angular/angular/blob/main/CHANGELOG.md
13. https://github.com/analogjs/analog · https://analogjs.org/docs/packages/astro-angular/overview · npm data 2026-10-18
14. https://astro.build/blog/astro-7/ (2026-06-22) · https://astro.build/blog/astro-740/ (2026-10-15) · https://astro.build/blog/astro-6/ (2026-03-10)
15. https://blog.cloudflare.com/astro-joins-cloudflare/ (2026-01-16)
16. https://docs.astro.build/en/guides/integrations-guide/lit/ · https://docs.astro.build/en/guides/actions/
17. https://vite.dev/blog/announcing-vite8 (2026-03-12)
18. https://angular.dev/tools/cli/build-system-migration
19. https://svelte.dev/blog/sveltekit-3-is-here (2026-10-01) · https://github.com/solidjs/solid/discussions/2995
20. npm downloads week 2026-10-10..16 (api.npmjs.org); GitHub API: xyflow/xyflow, Foblex/f-flow, artem-mangilev/ngx-vflow, Blazor-Diagrams/Blazor.Diagrams; NuGet: Z.Blazor.Diagrams
21. https://learn.microsoft.com/en-us/aspnet/core/blazor/components/quickgrid (2026-09-17)
