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

### Domains (proposal — owner to confirm names)
- `screenhire.com` → marketing (SSG/CDN)
- `app.screenhire.com` → dashboard; `api.screenhire.com` → API
- **Tenant careers pages on a separate registrable domain** (e.g., `{tenant}.<careers-domain>`), the
  github.io / myshopify.com pattern: isolates cookies and abuse reputation from the marketing domain, keeps thin job
  pages from diluting marketing-site SEO, and lets one Search Console domain property cover all tenant subdomains for
  the Indexing API. Custom customer domains as a paid feature.

## Consequences
- A Node runtime in production for `apps/careers` (accepted by owner).
- Marketing hosting cost ≈ $0 on a static CDN.
- Shared design tokens/components between marketing, careers and dashboard via `packages/ui` (ADR-0009).
- Astro's owner (Astro's company) was acquired by Cloudflare on 2026-01-16; MIT licence and "deploy anywhere" are stated
  commitments — we stay on the Node adapter / static output to remain host-agnostic.
