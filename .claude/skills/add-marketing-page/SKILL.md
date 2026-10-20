---
description: Add a page to the static marketing site in apps/marketing - Astro page through BaseLayout and the Seo component, JSON-LD, about 0 KB of client JS, sitemap and llms.txt, Warm Grid tokens, and Playwright checks. Use for any marketing, pricing, legal or content page.
argument-hint: [path - what the page is for]
---

# Add a marketing page

The marketing site is static (no adapter in `apps/marketing/astro.config.mjs`) and SEO-first. Every page goes through
`BaseLayout`, which renders `src/components/Seo.astro`. Read `.claude/rules/astro-public.md` first.
`src/pages/index.astro` is the reference page.

**Page:** $ARGUMENTS

## 1. Page

Create `apps/marketing/src/pages/<path>.astro` (or `<path>/index.astro` for a section):

```astro
---
import BaseLayout from '../layouts/BaseLayout.astro';

const title = 'Pricing for small hiring teams | ScreenHire';
const description = 'One flat price per job ...';
const jsonLd = [{ '@type': 'WebPage', name: title, description }];
---

<BaseLayout title={title} description={description} jsonLd={jsonLd}>
  <section aria-labelledby="page-title" class="mx-auto max-w-6xl px-6 pt-14 pb-16">
    <h1 id="page-title" class="font-display text-5xl font-extrabold tracking-tight">...</h1>
  </section>
</BaseLayout>
```

- `title` is unique and under about 60 characters; `description` is one or two sentences, under about 160.
- `jsonLd` holds page-specific schema.org objects. `Seo.astro` already adds `Organization` and `WebSite` and wraps
  everything in one `@graph`, so do not repeat those. Use `FAQPage`, `SoftwareApplication`, `Article` or
  `BreadcrumbList` where they fit the content.
- Canonical URLs come from `Astro.site` (`SITE_URL`), so never write a host into a link or a JSON-LD `url`. Build
  absolute URLs with `new URL(path, Astro.site)`.
- One `h1`, then `h2` and `h3` in order. Sections that are landmarks get `aria-labelledby`.
- Warm Grid tokens only (`bg-background`, `text-foreground`, `text-secondary-foreground`, `bg-primary`). No hex.
- No client JavaScript. The only `<script>` is the JSON-LD from `Seo.astro`.
- Claims about the product follow the non-negotiables: evidence not verdicts, no automatic rejection, no emotion or
  biometric inference, notice before collection. Claims about law cite `docs/research/legal.md` and are not legal
  advice.

## 2. Discovery

- The sitemap picks up the page automatically (`@astrojs/sitemap`).
- Add a nav or footer link in `src/layouts/BaseLayout.astro` if the page is a top-level section.
- If the page describes what ScreenHire does or its principles, update `public/llms.txt` to match.

## 3. Checks

```
pnpm --filter @screenhire/marketing check
pnpm --filter @screenhire/marketing build
find apps/marketing/dist -name '*.js'
grep -o '<script[^>]*>' apps/marketing/dist/<path>/index.html
```

The `find` prints nothing, and the `grep` prints only `<script type="application/ld+json">`.

## 4. Tests

- **Playwright end-to-end** in `tests/ScreenHire.E2E`, if that project exists: open the page through the AppHost and
  assert the `h1`, the title and description, the canonical link, that the JSON-LD parses, that no other `<script>`
  is present, and the axe check. Run `dotnet test --project tests/ScreenHire.E2E --filter-class "*<Page>Tests"`.
- If the project is missing on your branch, say so in your final summary and rely on the build checks above. The
  `test-builder` agent picks tiers.
