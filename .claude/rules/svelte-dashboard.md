---
paths:
  - "apps/dashboard/**"
  - "packages/ui/**"
---
# Rule: recruiter dashboard conventions

The dashboard is a SvelteKit 3 single-page app (ADR-0006), rendered in the browser and served as static files with a
`200.html` fallback. CLAUDE.md keeps the one-line version of these rules; this is the detail. The procedure for a
new route is the `/add-dashboard-page` skill.

## Svelte 5 and SvelteKit 3

- Runes only (`compilerOptions.runes: true` in `vite.config.ts`): `$props()`, `$state()`, `$derived()`,
  `$effect()` (rarely; prefer `$derived`). No `export let`, `$:`, `on:click` (use `onclick`), `<slot>` (use
  snippets and `{@render ...}`), or `createEventDispatcher` (pass callback props).
- Config lives in `vite.config.ts`; there is no `svelte.config.js`.
- Import from `#lib/...` with the `.js` extension (`#lib/components/ui/badge/index.js`), never `$lib`. The alias is
  the `imports` map in `apps/dashboard/package.json`.
- `$app/state` (`page`, `navigating`), never `$app/stores`. `$app/env` (`browser`, `dev`), never
  `$app/environment`. `tsconfig.json` extends `$app/tsconfig`.
- Pages type their props with `PageProps` from `./$types`. `export const ssr = false` is set once in the root
  `+layout.ts`; do not set it per page.

## Data

- Pages read and change data with TanStack Query (`@tanstack/svelte-query` v6), through the hooks Orval generates in
  `@screenhire/api-client/svelte-query`: `createListJobs()`, `createGetJob(() => params.jobId)`,
  `createMoveApplication(...)`. Their query functions throw `ApiError` on a status outside 2xx, so `query.isError` and
  `query.error.status` are the error state; `describeError` in `#lib/query.ts` words it. `query.data` is the response
  (`query.data.data` is the body). Never hand-write a `fetch` or a query key. The one exception is a file's bytes,
  which the generated client cannot read: the resume preview fetches the generated download URL itself.
- The `(app)` shell owns the one `QueryClient` (`#lib/query.ts`) and clears it on sign-out. Component tests render a
  page inside `src/test-support/with-query-client.svelte` and stub `fetch`.
- The root `+layout.ts` load (the auth guard) is the one SvelteKit `load` that calls the API, with the plain fetch
  client from `@screenhire/api-client`, which returns `{ data, status, headers }` and never throws.
- The Vite dev server proxies `/v1` to the API (WebSockets included, for the real-time hub), so the client uses
  relative URLs and stays same-origin for cookie auth. Never hard-code an API host.
- Live updates: the `(app)` shell holds the one hub connection (`#lib/realtime.ts`). `invalidations` maps each event
  to the API paths whose queries it makes stale (`/v1/jobs`), and `invalidatePath` invalidates every query under a
  path, because the generated query keys are the path's segments (`['v1', 'jobs', jobId]`). Never open a second
  connection in a page. Procedure: `/add-realtime-event`.
- Moving an application goes through `createMoves()` (`#lib/moves.svelte.ts`), which every move control shares: the
  menu, the board's keyboard path and drag-and-drop.

## UI

- How a screen looks, reads and behaves is `docs/design/design-system.md`, the one source of truth: its principles,
  review checklist (section 2), page templates (section 7) and component catalogue (section 11). This rule covers
  only the code conventions that serve it.
- shadcn-svelte components in `src/lib/components/ui` are our code: edit them freely, and keep ScreenHire variants
  there (the `clear`, `review`, `verify`, `ai`, `neutral`, `quiet`, `muted` and `count` badge variants). Add new ones with
  `pnpm exec svelte-kit sync` then `pnpm dlx shadcn-svelte@1.7.0 add <name> --yes --no-deps-install` from
  `apps/dashboard`. Use `field` with superforms 3 for forms, not the `form` component; use Sheet, not Drawer.
- Tokens only. Colours, fonts, type sizes and radii come from `@screenhire/ui` through Tailwind classes (`bg-card`,
  `text-muted-foreground`, `text-status-review`). Never a hex value, never `rgb(...)`, never a Tailwind palette
  colour such as `text-red-600`, never a pixel font size such as `text-[11px]`. Type sizes are the type tokens
  (`text-body`, `text-caption`, ...), never Tailwind's defaults (`text-sm`) or `tracking-*`. A new `--text-*` token
  also joins `typeScale` in `#lib/utils.ts`, or `cn()` reads it as a colour and drops it; a new named radius
  (`rounded-panel`) joins `radii` there for the same reason.
- Status is a shape mark, a colour and a text label, always all three (design system section 3.2). Colour alone
  fails WCAG 1.4.1.
- Integrity UI shows levels, reasons and evidence. It never shows a numeric score or a percentage, and it has no
  reject control (ADR-0004). No bulk action pairs an integrity filter with a stage that ends a candidacy: bulk moves
  take their stages from `bulkMoveStages` (`#lib/integrity.ts`).
- Accessibility is WCAG 2.2 AA: a visible focus ring on every interactive element, labels on every input, landmark
  and heading structure, and a non-drag alternative for every drag-and-drop interaction (a menu or buttons that move
  the item).
- Keyboard: Ctrl K / ⌘K opens the command palette. Single-key shortcuts (the G chords, J / K, X, V) respect
  `preferences.shortcutsEnabled` (WCAG 2.1.4: the person can turn them off), skip text fields, menus and dialogs
  (`isTypingTarget` in `#lib/shortcuts.ts`), and work only while their table or board has focus, except the G chords.
  Every shortcut has a visible control. Hints appear as key caps in tooltips, menus and the `?` dialog (design system
  section 15), never in an always-on bar. A new shortcut joins `shortcutGroups` in `#lib/shortcuts.ts`, which the `?`
  dialog lists.
- The candidate table (`#lib/components/application-table.svelte`) takes extra TanStack columns through
  `extraColumns`, between Stage and Source; build them with `candidateColumns` from `#lib/candidate-table.ts`. The
  integrity chips are `integrityColumns` (`#lib/integrity-columns.ts`). The table sorts (a column with an accessor gets
  a header button and `aria-sort`) but never filters: pages filter the rows they pass in (stage, saved view,
  `IntegrityFilter`, search with `matchesSearch`), so the count and "select all" match what is shown. Saved views are
  the list in `#lib/saved-views.ts`.
- Integrity levels are drawn from `#lib/integrity.ts` (label, shape, tone, badge variant) through `IntegrityChip`;
  evidence is drawn by its kind through `SignalEvidence`, never per signal code.

## Checks

- `pnpm --filter @screenhire/dashboard check` runs `svelte-check --fail-on-warnings`. A warning fails the gate.
- `src/tokens-only.test.ts` (Vitest) fails on a font size or letter spacing off the type tokens, a hex colour, a
  colour function such as `rgb(` or a Tailwind palette colour anywhere in `apps/dashboard/src`, the shadcn-svelte
  sources included.
