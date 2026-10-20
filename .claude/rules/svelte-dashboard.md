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

- Call the API only through the generated functions in `@screenhire/api-client` (`listJobs()`). They return
  `{ data, status, headers }` and never throw on non-2xx, so check `status` and turn failures into
  `error(status, message)` from `@sveltejs/kit`.
- The Vite dev server proxies `/v1` to the API (WebSockets included, for the real-time hub), so the client uses
  relative URLs and stays same-origin for cookie auth. Never hard-code an API host.
- Live updates: the `(app)` shell holds the one hub connection (`#lib/realtime.ts`). A page whose data an event
  changes declares `depends('api:<name>')` in its `load`, and `invalidations` in `#lib/realtime.ts` maps the event
  to that key. Never open a second connection in a page. Procedure: `/add-realtime-event`.

## UI

- shadcn-svelte components in `src/lib/components/ui` are our code: edit them freely, and keep ScreenHire variants
  there (the `clear`, `review`, `verify` and `ai` badge variants). Add new ones with
  `pnpm exec svelte-kit sync` then `pnpm dlx shadcn-svelte@1.7.0 add <name> --yes --no-deps-install` from
  `apps/dashboard`. Use `field` with superforms 3 for forms, not the `form` component; use Sheet, not Drawer.
- Tokens only. Colours, fonts and radii come from `@screenhire/ui` through Tailwind classes (`bg-card`,
  `text-muted-foreground`, `text-status-review`). Never a hex value, never `rgb(...)`, never a Tailwind palette
  colour such as `text-red-600`.
- Status is a shape mark, a colour and a text label, always all three (design-system section 2.2). Colour alone
  fails WCAG 1.4.1.
- Integrity UI shows levels, reasons and evidence. It never shows a numeric score or a percentage, and it has no
  reject control (ADR-0004).
- Accessibility is WCAG 2.2 AA: a visible focus ring on every interactive element, labels on every input, landmark
  and heading structure, and a non-drag alternative for every drag-and-drop interaction (a menu or buttons that move
  the item).

## Checks

- `pnpm --filter @screenhire/dashboard check` runs `svelte-check --fail-on-warnings`. A warning fails the gate.
