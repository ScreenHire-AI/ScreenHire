# Svelte 5 UI Libraries — Evaluation

> Research snapshot: **2026-10-18**. npm registry + downloads (week Oct 10–16), GitHub API (human contributors last
> 12 months, open issues excl. PRs). Probe builds: SvelteKit 3.1.0, Vite 8.3, Svelte 5.57.2, Tailwind 4.3, Astro 7.4.
> Sizes are brotli.

## 1. Is the Svelte ecosystem rich enough?

**Yes — every need on our list has a maintained Svelte 5 library. Thinner and younger than React's; roughly Angular's
size but community-run rather than first-party.**

Weekly downloads — frameworks: react 115.4M, vue 10.3M, **svelte 4.14M**, @angular/core 3.43M. Comparable libs: Radix
react-dialog 46.0M vs bits-ui 0.81M; @tanstack/react-query 40.6M vs svelte-query 0.19M.

**Strengths**
- Coverage: every category has a maintained Svelte 5 library; most released in the last 8 weeks (§6).
- Shared engines: heavy libraries are framework-agnostic (TanStack, Zag, Floating UI, Tiptap, pdf.js, Motion); Svelte's
  `{@attach}` wraps any plain-JS library in a few lines.
- Core well resourced: 105 Svelte + 93 SvelteKit releases in 12 months; Cloudflare acquired Astro (2026-01-16).
- Measured cost is low: an Astro page with no islands ships 0 `<script>` tags; a Svelte island on native controls is
  17.3 KB total.

**Weak spots**
- **Bus factor:** the default stack (bits-ui, shadcn-svelte, formsnap, runed, paneforge, vaul-svelte, mode-watcher)
  comes from one small group; huntabyte is the only npm publisher for bits-ui (~8× commits of next contributor).
- **Stale supporting packages:** shadcn Drawer depends on vaul-svelte `1.0.0-next.7` (2025-03-24); formsnap last
  released 2025-04, paneforge 2025-08, runed 2025-12.
- **SvelteKit 3 is 17 days old:** runed peer dep still `^2` (fix pending, runed PR #432); formsnap requires superforms 2.
- **No independent accessibility audits** for any Svelte library (vs React Aria 2.8M/wk, Angular CDK 2.27M/wk
  first-party). Open bits-ui ARIA bugs: Select trigger lacks `role`/`aria-controls` (#2134); options lack
  `aria-selected` (#2173); Combobox ignores VoiceOver click (#2185).
- **Few official Svelte wrappers for commercial widgets** (AG Grid none; `ag-grid-svelte5` 107/wk). SVAR is the exception.

## 2. Comparison

Bundle Δ = extra brotli over an 11.4 KB Svelte baseline (Select / Dialog / Select+Dialog+Combobox).

| Library | Covers our 24 needs | Accessibility | Theming | Bundle Δ | Health (dl/wk · last release · releases/12mo · contributors/12mo · open issues · funding) | Licence · no-JS / Astro |
|---|---|---|---|---|---|---|
| **shadcn-svelte + bits-ui** | ~24 (data table = TanStack v9 recipe; charts = LayerChart) | WAI-ARIA patterns; open bugs above | CSS variables → TW4 `@theme`; dark mode | 35.3 / 18.9 / 38.5 KB | bits-ui 805k · 10-03 · 36 · 46 · 55; shadcn 134k · 09-16 · 23 · 39 · 54; GitHub Sponsors | MIT · needs JS (islands only) |
| **Skeleton 5** (Zag) | ~18 (no data table, command palette, resizable panels, plain Select) | Zag state machines | Own theme format on TW4; CSS-only table/badge/dialog | — / 21.9 / D+C 42.3 | 35k · 08-19 · 54 · 18 (2 core) · 33 · Sponsors | MIT · partly |
| **Ark UI Svelte** (Zag) | ~19 (adds date range, splitter, drawer, file upload, tags input; no table/palette) | `@zag-js/select` static check: `role=combobox`, `aria-controls`, `aria-activedescendant`, `aria-selected` | Unstyled, `data-*` styling | 38.1 / 25.0 / 50.0 | **8.3k** · 10-05 · 25 · 26 (one lead) · 0 · Open Collective; 6.0-next started | MIT · islands |
| Park UI | **No Svelte version** | — | Panda CSS | — | preset last 2024-11 | drop |
| Melt next (`melt`) | ~12 (no menu, date picker, table) | a11y guide still an open issue | Unstyled | 25.2 / 7.6* / 26.9 | 5.7k · **01-04** · 3 · 2 · 18; ships `jest-axe` as runtime dep | MIT · not ready |
| **daisyUI 5** | Full CSS component set, no behaviour | As good as our markup; use native dialog/details/popover | Native TW4 plugin; CSS-variable themes | **0 JS**; page CSS 10.4 KB | 668k · 10-13 · 116 · 11 (1 core) · 20 · Open Collective | MIT · ideal |
| **SVAR** Grid / Gantt | Virtualised editable grid, Gantt | Claims WAI-ARIA + hotkeys | Own Willow themes (not Tailwind) | Grid 63.5 KB | Grid 8k / Gantt 0.7k · 10-12 · 12 · 3 · 30 / 6 · commercial company | MIT core; builds on Kit 3.1, runes source; print injects `<style>` (strict CSP); **Gantt PRO $749–6,799** |
| Flowbite Svelte | Broad | not assessed | TW4 | — | 59k · last stable **04-07**; v2 stuck at next.14 | skip |

*Melt Dialog wraps native `<dialog>` — not comparable.

**SvelteKit 3.1 probe findings**
- shadcn CLI: run `npx svelte-kit sync` before each `shadcn-svelte add` (else "$app/tsconfig not found" because
  `npm i` deletes `node_modules/$app`); imports must use `#lib/.../index.js` (#2917). With that, 30 components built in
  SPA mode.
- `add form` fails (ERESOLVE, #2915). Workaround: superforms 3.0.0 + npm `overrides` for formsnap → installs, builds,
  svelte-check 0 errors (runtime untested).
- runed peer dep → `npm ls` ELSPROBLEMS; six runed copies installed.
- Zag-based libs install cleanly (Skeleton 5.0.1, Ark 5.24.3, melt 0.44). Skeleton pins Zag 1.43, Ark 1.45 → both
  together duplicate Zag.
- Strict CSP: bits-ui 2.19.5 restores body styles via CSSOM (PR #2175); probe bundles show no `<style>` injection or
  `setAttribute('style')`; #1995 still open; SSR `style=""` attributes still break strict CSP (incl. daisyUI popover
  examples' inline `anchor-name`).
- Astro island: bits-ui Select island 50.7 KB vs 17.3 KB native controls.

## 3. Recommendation

**(a) Dashboard — shadcn-svelte on bits-ui.** Tokens map to TW4 `@theme`; sidebar, data-table and command-palette
blocks fit the precise-dashboard brief. Conditions:
- Pin bits-ui ≥ 2.19.5.
- Forms: `field` component + superforms 3 in SPA mode, not `form`.
- Sheet instead of Drawer.
- Patch missing ARIA (#2134, #2173) in our copied wrappers; #2185 needs upstream fix or patch-package.
- Use Ark component-by-component only where bits-ui is weak.
- Add TanStack Table v9 + Virtual, LayerChart, Svelte Flow, TanStack Query v6.

**(b) Candidate / careers pages — native HTML controls + our Tailwind tokens, with daisyUI 5 as a CSS-only layer.**
- Bind daisyUI theme variables to shared tokens (cream, coral).
- Svelte islands only for validation and upload, on native inputs.
- Not zero-JS: combobox and date range → `<input type=date>` or Cally (small web component). Invoker `commandfor` buttons
  reach only 86% of users → hashed fallback script. Customisable `<select>` reaches 70% (no Firefox) → enhancement only.

**Per-tenant brand colours** — computed server-side on save; reject or auto-adjust tone when contrast fails:

| Token | Purpose | Minimum contrast |
|---|---|---|
| `brand` | Tenant colour | — |
| `on-brand` | Text on a brand fill | 4.5:1 |
| `brand-text` | Brand-coloured text on page background (WCAG 1.4.3) | 4.5:1 |
| `brand-ui` | Borders, focus rings (WCAG 1.4.11) | 3:1 |

Ship as a per-tenant CSS file or hashed `<style>`, never inline. CSS `contrast-color()` (82.8% support) as enhancement only.

## 4. How to review (click-through)

1. Dashboard look: https://shadcn-svelte.com/examples/dashboard · https://shadcn-svelte.com/blocks · https://shadcn-svelte.com/themes · https://shadcn-svelte.com/create
2. Tables: https://shadcn-svelte.com/docs/components/data-table · https://svar.dev/demos/grid/ · https://svar.dev/demos/gantt/
3. Keyboard/screen-reader feel: https://bits-ui.com/docs/components/combobox · https://ark-ui.com/svelte/docs/components/date-picker
4. Skeleton: https://www.skeleton.dev/docs/svelte/framework-components/combobox · https://themes.skeleton.dev
5. Candidate side: https://daisyui.com/components/ · https://daisyui.com/theme-generator/
6. Charts + graph editor: https://layerchart.com · https://svelteflow.dev/examples
7. Melt next: https://next.melt-ui.com

## 5. Risks & mitigations

| Risk | Mitigation |
|---|---|
| Core UI stack depends on one maintainer | We own copied component source; Ark can replace individual components; sponsor the maintainer |
| SvelteKit 3 churn | Pin versions + npm overrides; re-check #2915 and runed #432 monthly |
| A11y gaps, no external audits | axe in CI, manual NVDA + VoiceOver passes, external WCAG 2.2 audit before GA |
| Strict CSP | bits-ui ≥ 2.19.5, Astro style hashes, no inline `style`, test #1995 scenarios |
| Stale Drawer / resizable-panel packages | Sheet; fall back to Ark Splitter |
| daisyUI single maintainer | CSS-only → cheap to leave |
| Two token vocabularies (shadcn + daisyUI) | Generate both from one token source |
| Tenant colours failing contrast | Enforce server-side; visual regression tests per theme |

## 6. Ecosystem map (version · dl/wk · last release; 2026 unless shown)

| Need | Package |
|---|---|
| Node-graph editor | @xyflow/svelte 1.7.0 · 167k · 09-24 |
| Drag-and-drop / kanban | svelte-dnd-action 0.9.80 · 155k · 10-05; alt @dnd-kit/svelte 0.5.1 (pre-1.0) · 11k · 10-15 |
| Tables / virtualisation | @tanstack/svelte-table 9.3.1 · 72k · 10-12; @tanstack/svelte-virtual 3.13.47 · 51k · 10-16 |
| Charts | layerchart 2.8.1 · 185k · 10-17 (avoid @unovis/svelte — still Svelte 4) |
| Rich text | Tiptap core 3.32 via `{@attach}` · 13.6M · 10-13; or svelte-tiptap 3.0.2 · 25k · 10-02 |
| Date pickers | bits-ui Calendar / RangeCalendar 2.19.5 |
| Scheduling calendar | @event-calendar/core 5.17.2 · 48k · 10-16 |
| Forms + validation | sveltekit-superforms 3.0.0 · 163k · 10-04 (zod 4.6 / valibot 1.5); alt @tanstack/svelte-form 1.33.5 · 17k · 08-11 |
| Toasts | svelte-sonner 1.2.1 · 385k · 08-14 |
| Icons | @lucide/svelte 1.59.0 · 631k · 10-17 |
| Animation | built-in transitions + `motion` 14.3 via `{@attach}` · 15.5M · 10-15 |
| i18n | @inlang/paraglide-js 2.29.0 · 519k · 10-15 (svelte-i18n stale since 2024-10) |
| PDF viewing | pdfjs-dist 6.4 via `{@attach}` · 20M · 10-03; or @embedpdf/svelte-pdf-viewer 2.15.1 · 2.7k · 09-16 |
| File upload | @uppy/svelte 6.0.1 · 6k · 10-16 |
| Command palette | shadcn Command (bits-ui) (cmdk-sv stale since 2025-03) |
| Data fetching | @tanstack/svelte-query 6.3.4 · 188k · 10-17 (runes-native) |
| State | runes in `.svelte.ts` + runed 0.37.1 · 1.93M · 2025-12-20 |

Also easy via `{@attach}`: Floating UI, ECharts 6.1, @dnd-kit/dom, AG Grid Community (`createGrid`).
