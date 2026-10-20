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
  PR says why. The only `<script>` on a page is JSON-LD (`type="application/ld+json"`).
- **The embeddable apply widget is an iframe**, never Shadow DOM or a script that injects into the host page.
- **SEO on every page.** Marketing pages pass `title`, `description` and optional `jsonLd` to `BaseLayout`, which
  renders `Seo.astro` (canonical URL, Open Graph, Twitter card, Organization and WebSite JSON-LD). Careers pages pass
  `title` and `description` to `CareersLayout` and add their JSON-LD through its `head` slot. A job page carries a
  schema.org `JobPosting` (`docs/architecture/job-distribution.md`).
- **Domains are configuration.** Marketing uses `Astro.site` (from `SITE_URL`); careers reads `API_URL` and, for
  tenant hosts, the `ScreenHire:Domains:*` settings. Never write a `screenhire.*` host into a page (ADR-0003).
- **Tokens only.** Marketing uses the Warm Grid tokens (`bg-background`, `text-primary`); careers uses the candidate
  tokens (`bg-candidate-background`, `bg-candidate-cta`). No hex values.
- **Notice before collection.** Browsing a careers or marketing page collects server and security logs only. No
  analytics beacons, fingerprinting or form telemetry before an application starts and the notice is shown, and no
  third-party script tags at all.
- **Careers data comes from the API** through `apps/careers/src/lib/api.ts` (the generated client with the server
  base URL), and only on the server. Unknown organization or job returns `new Response(null, { status: 404 })`; a
  closed job returns 410.
- **Accessibility:** WCAG 2.2 AA. One `h1`, headings in order, `aria-label` or `aria-labelledby` on sections that
  need a name, visible focus, and real links and buttons.

## Checks

- `pnpm --filter @screenhire/marketing check` and `pnpm --filter @screenhire/careers check` (astro check).
- After a build, `find apps/marketing/dist apps/careers/dist/client -name '*.js'` prints nothing unless the PR adds
  an island on purpose.
