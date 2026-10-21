---
description: Add a recruiter-dashboard route in apps/dashboard - SvelteKit 3 page with runes, #lib imports and $app/state, shadcn-svelte components, data from the generated API client, loading/empty/error states, and Vitest plus Playwright tests. Use for any new dashboard screen.
argument-hint: [route path - what it shows]
---

# Add a dashboard page

Read `.claude/rules/svelte-dashboard.md` first. `apps/dashboard/src/routes/(app)/+page.svelte` (the jobs table) and
`(app)/jobs/[jobId]/+page.svelte` (the job's pipeline) are the reference pages for code. How the page looks and reads
comes from `docs/design/design-system.md`, the one source of truth; step 2 applies it.

**Page:** $ARGUMENTS

## 1. API first

If the data the page needs has no endpoint yet, add it with `/add-endpoint` and regenerate the client before you
write the page. The page imports only generated functions and types from `@screenhire/api-client`.

## 2. Design first

Read sections 1, 2, 7 and 8 of `docs/design/design-system.md`. Before writing markup, decide, and put in your final
summary:

- the page template (section 7) and its header band: title, status badge, the one primary action, facts or tabs;
- the panels in priority order, each with its title, and what each holds;
- what changes on a tablet and a phone (section 8).

Build from the component catalogue (section 11). A pattern it does not have joins section 11 in the same change.

## 3. Route files

Signed-in pages live under `apps/dashboard/src/routes/(app)/`. `<path>/+page.svelte` queries its own data with the
generated TanStack Query hooks; there is no `+page.ts`:

```svelte
<script lang="ts">
  import { createGetJob } from '@screenhire/api-client/svelte-query';
  import type { PageProps } from './$types';

  let { params }: PageProps = $props();
  const job = createGetJob(() => params.jobId);
  const detail = $derived(job.data?.data);
</script>
```

- Route params come from `PageProps`; derived values with `$derived(...)`. Mutations use the generated
  `create<Operation>` hooks with `onSettled: () => invalidatePath(queryClient, jobsPath)` (`#lib/query.ts`).
- `<svelte:head><title>Page · ScreenHire</title></svelte:head>`, the `TopBar` with the page's breadcrumbs, one `h1`,
  and the `main` structure of the jobs page.
- Imports from `#lib/...` with `.js` extensions. `page` and `navigating` come from `$app/state`.
- Add the route to `sections` in `#lib/shell.ts` if it is a top-level section: the sidebar, its G shortcut and the
  command palette all read that list.

## 4. States

Every page handles all four:

- **Loading.** While `query.isPending`, render `Skeleton` placeholders of the same shape (`#lib/components/ui/skeleton`),
  `aria-hidden`, with a visually hidden "Loading…" status where nothing else says so.
- **Empty.** An `{:else}` branch on the `{#each}` with one line that says what is missing and, when the user can fix
  it, the action ("No jobs yet." plus a "Create job" button).
- **Error.** When `query.isError`, render `QueryError` (`#lib/components/query-error.svelte`) with
  `describeError(query.error, 'Could not load …')` and a retry. A 404 (`query.error.status`) gets its own heading
  and a link back. Never show raw response bodies or stack traces.
- **Success.** The real content.

## 5. Components

- Build the page from the catalogue in design system section 11: `HeaderBand` and `Fact`, `Panel`, `StatusBadge`,
  `SummaryList`, `FileTile`, `ResumePreview`, `ActivityList`, `ListItem`, `RelativeTime`, `TruncatedText` in
  `#lib/components`. The
  style guide (`/style-guide` while `pnpm dev` or `/preview` runs) shows each one in every state; compare your page
  against it.
- Use the shadcn-svelte components in `#lib/components/ui`. To add one, from `apps/dashboard`:

  ```
  pnpm exec svelte-kit sync
  pnpm dlx shadcn-svelte@1.7.0 add <name> --yes --no-deps-install
  ```

  `--yes` still stops to ask before it overwrites a component we already have (sidebar brings button, sheet and
  input). Add `--overwrite` instead, then `git checkout --` each existing component folder it changed, so our edits
  stay. Then check `git diff package.json`. If the component needs a package that is not installed, add it with
  `pnpm --filter @screenhire/dashboard add -D <package>` and run the `licence-check` agent.
- Status cells use `StatusBadge`: a shape mark, colour and text label (`clear`, `review`, `verify`, `ai`), with
  `quiet` for calm states in a table. Never colour alone.
- Tokens only (`bg-card`, `text-muted-foreground`). No hex, no palette colours, no pixel font sizes.
- Integrity data shows levels, reason codes and evidence; never a score, and no reject control.

## 6. Tests

- **Vitest** for component logic and rendering, if the dashboard has a `test` script in `package.json`
  (`@testing-library/svelte` on jsdom; see `docs/process/testing.md`). Run `pnpm --filter @screenhire/dashboard test`.
- **Playwright end-to-end** in `tests/ScreenHire.E2E`, if that project exists: a test that opens the page through
  the AppHost, asserts rendered content (row text, headings, the empty state), not just a status code, and runs the
  axe check. Run it with `dotnet test --project tests/ScreenHire.E2E --filter-class "*<Page>Tests"`.
- If either tier is missing on your branch, note it in your final summary and cover the logic at the tier that
  exists. Use the `test-builder` agent to pick tiers and write the tests.

## 7. Done when

```
pnpm --filter @screenhire/dashboard check
pnpm --filter @screenhire/dashboard build
```

both pass with no warnings, the tests above pass, and the page works with the keyboard alone (tab order, visible
focus, Enter and Space on controls).

Then hold the page to the design system: run the `design-review` agent on your changes and fix what it reports, and
look at the page with `/preview` at 1440, 768 and 390px wide (`kangentic_browser_set_viewport`). The review checklist
(section 2) must hold at all three.
