---
paths:
  - "apps/careers/**"
  - "apps/marketing/**"
---
# Rule: public pages (careers and marketing)

Public pages are Astro (ADR-0003). The marketing site is static and SEO-first; the careers pages render on demand
on Node so a closed job can return 410 at once. Candidates arrive on phones over slow networks, and search engines
rank fast pages, so both ship about zero kilobytes of JavaScript. The procedures are the `/add-careers-page` and
`/add-marketing-page` skills.

## The rule

- **No client JavaScript by default.** Astro components and native HTML (`<details>`, `<dialog>`, forms that post).
  A Svelte island (`client:visible`, `client:idle`) only where the page needs behaviour HTML cannot give, and the
  PR says why. The one island today is the apply form's `FileField.svelte` (direct-to-storage upload, from
  `@screenhire/ui/questions`), and the page works without it. The other shared question components render on the
  server only, with no client directive. The apply page also runs the telemetry module (`packages/telemetry`,
  framework-free, under 5 KB) from a processed `<script>`; the server starts it only with the assistive-technology
  switch off. Other than these, the only `<script>` on a page is JSON-LD (`type="application/ld+json"`).
- **Careers forms are native forms posted to an Astro Action** and must work with JavaScript off
  (`docs/architecture/apply-flow.md`). The apply page stays within about 22 KB of brotli JavaScript: about 20 KB for
  the island and Svelte, plus the telemetry module (an end-to-end test measures it). Islands call careers endpoints with
  a bare `fetch` rather than the actions client to stay there.
- **Strict content security policy on careers pages.** `security.csp` in `astro.config.mjs` plus the header from
  `src/middleware.ts`: no inline styles (`style=` attributes or `<style>`), no `unsafe-inline`, no third-party hosts
  except what a page adds with `Astro.csp` when configured (Turnstile). Employer colours come only from the hashed
  brand stylesheet.
- **The embeddable apply widget is an iframe**, never Shadow DOM or a script that injects into the host page.
- **SEO on every page.** Marketing pages take `title` and `description` from `src/lib/pages.ts` (or collection
  frontmatter) and pass them with `breadcrumbs` and `jsonLd` to `BaseLayout`, which renders `Seo.astro` (canonical
  URL, hreflang, Open Graph image, Twitter card, Organization and WebSite JSON-LD). Careers pages pass
  `title` and `description` to `CareersLayout` and add their JSON-LD through its `head` slot. A job page carries a
  schema.org `JobPosting` (`docs/architecture/job-distribution.md`).
- **Domains are configuration.** Marketing uses `Astro.site` (from `SITE_URL`); careers reads `API_URL`, and tenant
  hosts come from environment configuration when they are needed. Never write a `screenhire.*` host into a page
  (ADR-0003).
- **Tokens only.** Marketing uses the Warm Grid tokens (`bg-background`, `text-primary`); careers uses the candidate
  tokens (`bg-candidate-background`, `bg-candidate-cta`). No hex values.
- **Notice before collection.** Browsing a careers or marketing page collects server and security logs only. No
  analytics beacons, fingerprinting or form telemetry before an application starts and the notice is shown, and no
  third-party script tags at all.
- **One exception: cookieless analytics on the marketing site** (ADR-0003). `apps/marketing/src/components/Analytics.astro`
  renders a Plausible or Umami script only when `PUBLIC_ANALYTICS` names a provider and `PUBLIC_ANALYTICS_SCRIPT_PATH`
  is a site-relative path (a self-hosted instance or same-origin proxy, never a third-party host). It is off by
  default and in every test. Careers pages, the apply flow and the dashboard never load analytics. Turning it on is
  an owner decision: it needs a marketing privacy page that lists it, a provider configured to set no cookies or
  local storage and keep no raw IP addresses, and a check of the ePrivacy position per market (EDPB Guidelines 2/2023
  cover any script that reads from the device, cookie or not).
- **Careers data comes from the API** through `apps/careers/src/lib/api.ts` (the generated client with the server
  base URL), and only on the server. An unknown organization returns `new Response(null, { status: 404 })`; an unknown
  job is a 404 and a closed one a 410 (the API says which), both rendered with `JobUnavailable.astro`.
- **Links go through the tenant base.** On a tenant host (`{tenant}.{CareersDomain}`) pages have no `/{organization}`
  prefix; build every link from `basePath(Astro.request, organization)` (`src/lib/host.ts`). An API route (`.ts`
  endpoint) reads its organization with `organizationOf(request, params)`, because its `params` are stale after the
  tenant-host rewrite.
- **Accessibility:** WCAG 2.2 AA. One `h1`, headings in order, `aria-label` or `aria-labelledby` on sections that
  need a name, visible focus, and real links and buttons.

## Checks

- `pnpm --filter @screenhire/marketing check` and `pnpm --filter @screenhire/careers check` (astro check).
- After a build, `find apps/marketing/dist -name '*.js'` prints nothing, and `apps/careers/dist/client` holds only the
  apply page's island and telemetry chunks unless the PR adds an island on purpose.
- `pnpm --filter @screenhire/marketing build` fails on more than 15 KB of JS per page or a broken internal link, and
  `pnpm --filter @screenhire/marketing lighthouse` checks mobile LCP and CLS (both run in `pnpm verify`).
