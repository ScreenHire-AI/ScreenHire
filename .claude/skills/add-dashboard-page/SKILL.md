---
description: Add a recruiter-dashboard route in apps/dashboard - SvelteKit 3 page with runes, #lib imports and $app/state, shadcn-svelte components, data from the generated API client, loading/empty/error states, and Vitest plus Playwright tests. Use for any new dashboard screen.
argument-hint: [route path - what it shows]
---

# Add a dashboard page

Read `.claude/rules/svelte-dashboard.md` first. `apps/dashboard/src/routes/(app)/+page.svelte` (the jobs table) and
`(app)/jobs/[jobId]/+page.svelte` (the job's pipeline) are the reference pages. The visual language is `docs/design/design-system.md`.

**Page:** $ARGUMENTS

## 1. API first

If the data the page needs has no endpoint yet, add it with `/add-endpoint` and regenerate the client before you
write the page. The page imports only generated functions and types from `@screenhire/api-client`.

## 2. Route files

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

## 3. States

Every page handles all four:

- **Loading.** While `query.isPending`, render `Skeleton` placeholders of the same shape (`#lib/components/ui/skeleton`),
  `aria-hidden`, with a visually hidden "Loading…" status where nothing else says so.
- **Empty.** An `{:else}` branch on the `{#each}` with one line that says what is missing and, when the user can fix
  it, the action ("No jobs yet." plus a "Create job" button).
- **Error.** When `query.isError`, render `QueryError` (`#lib/components/query-error.svelte`) with
  `describeError(query.error, 'Could not load …')` and a retry. A 404 (`query.error.status`) gets its own heading
  and a link back. Never show raw response bodies or stack traces.
- **Success.** The real content.

## 4. Components

- Use the shadcn-svelte components in `#lib/components/ui`. To add one, from `apps/dashboard`:

  ```
  pnpm exec svelte-kit sync
  pnpm dlx shadcn-svelte@1.7.0 add <name> --yes --no-deps-install
  ```

  Then check `git diff package.json`. If the component needs a package that is not installed, add it with
  `pnpm --filter @screenhire/dashboard add -D <package>` and run the `licence-check` agent.
- Status cells use the badge variants with a shape mark and a text label (`clear`, `review`, `verify`, `ai`). Never
  colour alone.
- Tokens only (`bg-card`, `text-muted-foreground`, `font-mono`). No hex, no palette colours.
- Integrity data shows levels, reason codes and evidence; never a score, and no reject control.

## 5. Tests

- **Vitest** for component logic and rendering, if the dashboard has a `test` script in `package.json`
  (`@testing-library/svelte` on jsdom; see `docs/process/testing.md`). Run `pnpm --filter @screenhire/dashboard test`.
- **Playwright end-to-end** in `tests/ScreenHire.E2E`, if that project exists: a test that opens the page through
  the AppHost, asserts rendered content (row text, headings, the empty state), not just a status code, and runs the
  axe check. Run it with `dotnet test --project tests/ScreenHire.E2E --filter-class "*<Page>Tests"`.
- If either tier is missing on your branch, note it in your final summary and cover the logic at the tier that
  exists. Use the `test-builder` agent to pick tiers and write the tests.

## 6. Done when

```
pnpm --filter @screenhire/dashboard check
pnpm --filter @screenhire/dashboard build
```

both pass with no warnings, the tests above pass, and the page works with the keyboard alone (tab order, visible
focus, Enter and Space on controls).
