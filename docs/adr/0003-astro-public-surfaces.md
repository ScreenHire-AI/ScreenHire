# ADR-0003: Astro for all public, SEO-critical surfaces (marketing site + careers pages)

- **Status:** Accepted (2026-10-18)
- **Deciders:** Owner
- **Research:** `docs/research/frontend.md`

## Context
Two public surfaces live or die by speed and discoverability:
1. **Marketing site** — the owner's directive: *SEO and discoverability are first-class; marketing is imperative.*
2. **Careers pages + apply flow** — tenant job pages indexed by Google for Jobs, opened mostly on mobile, hosting the
   behavioural-telemetry module.

Astro sites have the highest mobile Core Web Vitals pass rate of the frameworks measured (71.1%, HTTP Archive Sept
2026), ship zero JS by default, and support islands. Blazor static SSR was the only other ~0 KB option; Astro won on
islands, Vite DX and image tooling. Angular SSR cannot reach zero JS (64–85 KB br hydrated per page).

## Decision

### `apps/marketing` — Astro, **static generation (SSG)**, served from a CDN
- **Technical SEO baseline** (CI-enforced):
  - `@astrojs/sitemap`, canonical URLs, `robots.txt`, clean URL structure, 301 redirect map.
  - `hreflang` for `en-US` / `en-GB` from launch; `de`, `fr` later.
  - JSON-LD: `Organization`, `WebSite` (+ SearchAction), `SoftwareApplication` (with offers/pricing),
    `FAQPage`, `BreadcrumbList`, `Article`/`BlogPosting`.
  - Build-time Open Graph images; RSS for the blog.
  - **Performance budgets**: ≤ 15 KB br JS per marketing page; mobile LCP < 1.8 s, CLS < 0.05, INP < 150 ms;
    Lighthouse CI fails the build on regressions.
    *Amended 2026-10-21:* `apps/marketing/scripts/check-dist.mjs` enforces the JS budget on every build. Lighthouse
    (`pnpm verify:lighthouse`, not Lighthouse CI) enforces LCP and CLS once per marketing change, in the Testing
    column before merge, one run at a time on the machine. INP is not checked yet; the pages ship no JS but JSON-LD.
    See `docs/process/testing.md`, "Marketing site checks".
  - **AI-search visibility**: `llms.txt`, clear entity/definition pages, concise FAQ blocks — buyers increasingly
    discover tools through AI assistants.
- **Content**: Astro content collections (Markdown/MDX) in the repo, with a git-based CMS (candidate: Keystatic) so
  non-developers can publish without a database or CMS bill.
- **Analytics**: cookieless, privacy-friendly analytics (e.g., Plausible/Umami/Cloudflare Web Analytics) — consistent
  with our privacy brand and avoids a consent banner for analytics in the EU.
- **Content strategy hooks** (to be detailed in a marketing plan):
  - Pillar pages: AI-generated applicants, candidate fraud, hiring-AI compliance.
  - **Hiring-AI law tracker** (NYC LL144, Illinois, Colorado, California ADMT, EU AI Act, UK) — link-earning content
    that reinforces our compliance moat.
  - Comparison pages ("GoHire alternative", "Breezy vs ScreenHire", …).
  - Free tools as lead magnets, e.g. a **resume prompt-injection checker** and a job-post compliance checker, built
    within our own privacy principles.

### `apps/careers` — Astro, **on-demand SSR** (Node adapter) behind the CDN
- Tenant job pages change constantly and teardown needs an instant `410` + Indexing API `URL_DELETED`, so pages render
  on demand with CDN caching and purge-on-change, not per-post rebuilds.
- `JobPosting` JSON-LD on every job page; per-tenant sitemaps.
- Apply form works as **plain HTML forms** (Astro Actions) with the framework-free `packages/telemetry` module; islands
  only where the questionnaire needs rich interaction.
- Embeddable widget = **iframe** served from our origin (host pages must not be able to forge telemetry or read PII).

### Domains — configurable, never hard-coded
Status (2026-10-18): **`screenhire.com` and `screenhire.co` are taken.** **`screenhire.io` is the planned primary**
(matching the gohire.io pattern); `.ai` / `.app` and a `.com` redirect (e.g. `getscreenhire.com`) are candidates.
Purchase is tracked in its own board task. Note: in British English "screen hire" also means renting a screen, which
affects UK search results — marketing copy should always pair the name with "hiring platform".

Every host is configuration (`ScreenHire:Domains:*` in .NET, `PUBLIC_*` env in the Astro/Svelte apps), used for
careers-page hosts, cookie domains, email sending domain, OAuth redirect URIs and CORS:

| Setting | Planned value | Serves |
|---|---|---|
| `BaseDomain` | `screenhire.io` | Marketing site (SSG/CDN) |
| `AppHost` | `app.{BaseDomain}` | Dashboard (same apex as API for cookie auth) |
| `ApiHost` | `api.{BaseDomain}` | Public API |
| `CareersHostPattern` | `{tenant}.{CareersDomain}` | Tenant careers pages |
| `CareersDomain` | **open decision** — `{BaseDomain}` (e.g. `acme.screenhire.io`) or a separate registrable domain | |

Recommendation for `CareersDomain`: a **separate registrable domain** (the github.io / myshopify.com pattern) isolates
cookies and abuse reputation from the product domain, keeps thin job pages from diluting marketing-site SEO, and one
Search Console domain property still covers all tenant subdomains for the Indexing API. Using `{tenant}.screenhire.io`
is simpler and also works (cookies must then be host-only on careers pages). Because it is configuration, the choice can
be made when the domains are bought. Custom customer domains remain a paid feature.

## Consequences
- A Node runtime in production for `apps/careers` (accepted by owner).
- Marketing hosting cost ≈ $0 on a static CDN.
- Shared design tokens/components between marketing, careers and dashboard via `packages/ui` (ADR-0009).
- Astro's owner (Astro's company) was acquired by Cloudflare on 2026-01-16; MIT licence and "deploy anywhere" are stated
  commitments — we stay on the Node adapter / static output to remain host-agnostic.
