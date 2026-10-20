---
description: Add a tenant careers page in apps/careers - Astro SSR route with about 0 KB of client JS, CareersLayout title/description/head slot, API data on the server, 404/410 handling, JSON-LD (JobPosting), candidate tokens, notice-before-collection, and Playwright checks. Use for any candidate-facing page.
argument-hint: [route - what it shows]
---

# Add a careers page

Careers pages are what candidates see first, often on a phone, and what Google for Jobs indexes. They render on
demand on Node (`output: 'server'` in `apps/careers/astro.config.mjs`) so a closed job disappears at once. Read
`.claude/rules/astro-public.md` first. `src/pages/[organization]/jobs/[job].astro` is the reference page.

**Page:** $ARGUMENTS

## 1. Route

- Tenant pages live under `apps/careers/src/pages/[organization]/`. Read params with `Astro.params`.
- Fetch on the server through `../lib/api` (relative to the page), which configures the generated client with
  `API_URL`. Never call `fetch` on an API route directly, and never from the browser.
- Fetch independent data with `Promise.all`.
- If the organization does not exist, `return new Response(null, { status: 404 });` before rendering. For a job, the
  API answers 404 (unknown or draft) or 410 (closed, with its last content): set `Astro.response.status` and render
  `JobUnavailable.astro`, passing a closed job's expired JSON-LD through its `head` slot, as `jobs/[job].astro` does.
- Build links from `basePath(Astro.request, organization)` (`src/lib/host.ts`), never `/${organization}`
  by hand: on a tenant host there is no prefix. Pass the organization's `branding` to `CareersLayout`.
- If the page needs data the API lacks, add a public endpoint under `/v1/public/...` with `/add-endpoint` first.

## 2. Layout and SEO

```astro
<CareersLayout
  title={`${job.title} | ${employer.name}`}
  description={`${job.title} at ${employer.name}. ${job.location}.`}
  organizationName={employer.name}
  organizationSlug={employer.slug}
  branding={employer.branding}
>
  <script slot="head" type="application/ld+json" is:inline set:html={JSON.stringify(jsonLd)} />
  ...
</CareersLayout>
```

- Every page has a unique `title` and a `description` written for a candidate.
- Job pages carry schema.org `JobPosting` JSON-LD with the fields Google for Jobs requires (title, description,
  datePosted, hiringOrganization, a location or `jobLocationType: 'TELECOMMUTE'`, and `baseSalary` when the job has
  one). See `docs/architecture/job-distribution.md`. A list page may carry an `ItemList`.
- If a page needs canonical or Open Graph tags, add them to `CareersLayout` for every page, not to one page.

## 3. Content rules

- Candidate tokens only: `bg-candidate-background`, `text-candidate-foreground`, `bg-candidate-cta`,
  `text-candidate-muted-foreground`, `border-candidate-border`. No hex values.
- No client JavaScript. Links, native forms (posted to an Astro Action, as the apply flow does) and `<details>`. The only
  `<script>` is the JSON-LD one, unless the page needs an island such as `FileUpload.svelte`.
- No inline styles: the content security policy forbids them. Employer colours come from the brand tokens
  (`bg-candidate-cta`, `text-candidate-brand-text`), never a computed style.
- Browsing collects nothing beyond server logs: no analytics, fingerprinting or telemetry scripts. Those start only
  after the candidate starts an application and has seen the notice (CLAUDE.md non-negotiable 4).
- Copy follows the design system voice: plain, honest about what ScreenHire checks, and clear that a person reviews
  every application. Never say an application can be rejected automatically.
- Labels for enums go through `label()` and money through `formatSalary()` in `src/lib/api.ts`.
- Use the organization's slug and name from the API. Link with the API's absolute URLs (`board.url`, `job.url`),
  never `/${slug}/...`: they are right on a tenant host (`src/middleware.ts`) and on the path-routed local board.
  Tenant hosts come from configuration, never a literal domain.

## 4. Checks

```
pnpm --filter @screenhire/careers check
pnpm --filter @screenhire/careers build
find apps/careers/dist/client -name '*.js'
```

The `find` lists only the apply page's island chunks. If you added an island on purpose, the PR says why.

## 5. Tests

- **Vitest** for helpers in `src/lib` if `apps/careers/package.json` has a `test` script: run
  `pnpm --filter @screenhire/careers test`.
- **Playwright end-to-end** in `tests/ScreenHire.E2E`, if that project exists: open the page through the AppHost
  and assert the rendered content, the JSON-LD (parse it and check `@type` and the required fields), the 404 for an
  unknown organization or item, that no `<script>` other than JSON-LD is present, and the axe check. Run
  `dotnet test --project tests/ScreenHire.E2E --filter-class "*<Page>Tests"`.
- Status codes and API data are also worth an Aspire integration test against the endpoint itself.
- If a tier is missing on your branch, say so in your final summary. The `test-builder` agent picks tiers.
