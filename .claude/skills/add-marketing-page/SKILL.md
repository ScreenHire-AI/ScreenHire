---
description: Add a page to the static marketing site in apps/marketing - Astro page through BaseLayout and the Seo component, the page registry, JSON-LD, Open Graph image and llms.txt from the site index, about 0 KB of client JS, Warm Grid tokens, budgets and Playwright checks. Use for any marketing, pricing, legal or content page.
argument-hint: [path - what the page is for]
---

# Add a marketing page

The marketing site is static (no adapter in `apps/marketing/astro.config.mjs`) and SEO-first. Every page goes through
`BaseLayout`, which renders `src/components/Seo.astro` (canonical, hreflang, Open Graph, one JSON-LD `@graph`). Read
`.claude/rules/astro-public.md` first. Start from the shell components in `src/components/shell` (`Section`, `DarkBand`, `SellingPoint`, `ProductFrame`,
`SourcedNumber`, `PlanRows`, `QuestionsList`, `ClosingBand`, `PageIntro`, `PageHero`, `RuledList`, `ProofList`, `CtaLink`), and read its words from
`docs/design/marketing-site.md` and `src/lib/features.ts`. How the page looks follows `docs/design/design-system.md`
section 22 and the Public pages group of its review checklist (section 2); check the page against both before you
finish. A page with a dark hero passes `headerTone="dark"` to `BaseLayout`.

**Page:** $ARGUMENTS

## 1. Content page or collection entry?

- A law tracker entry, blog post or comparison is a content-collection file, not a page: add
  `src/content/laws/<id>.md`, `src/content/blog/<id>.md` or `src/content/comparisons/<id>.yaml`. The schemas in
  `src/content.config.ts` say which fields are required (law entries need `published`, `lastReviewed` and `sources`).
  The routes under `src/pages/hiring-ai-laws/`, `blog/` and `compare/` render them. Skip to step 3.
- Anything else is a page: step 2.

## 2. Page

Register it in `apps/marketing/src/lib/pages.ts` (path with a trailing slash, title, description, label), then create
`apps/marketing/src/pages/<path>.astro`:

```astro
---
import ClosingBand from '../components/shell/ClosingBand.astro';
import Section from '../components/shell/Section.astro';
import BaseLayout from '../layouts/BaseLayout.astro';
import { PUBLIC_APP_URL } from 'astro:env/server';
import { appLinks } from '../lib/app-links';
import { pages } from '../lib/pages';
import { faqPage, type Faq } from '../lib/seo';

const { title, description, path, label } = pages.security;
const { signUp } = appLinks(PUBLIC_APP_URL, path);
const faqs: Faq[] = [{ question: '...', answer: '...' }];
---

<BaseLayout title={title} description={description} breadcrumbs={[{ name: label, path }]} jsonLd={[faqPage(faqs)]}>
  <Section labelledby="first-title">
    <h1 id="first-title" class="font-display text-section text-balance">...</h1>
  </Section>
  <ClosingBand title="..." href={signUp} />
</BaseLayout>
```

- The title is unique and under about 60 characters; the description is one or two sentences, under about 160.
  `src/lib/pages.test.ts` enforces both.
- `breadcrumbs` renders the visible trail and the `BreadcrumbList` JSON-LD. `jsonLd` takes the other page-specific
  nodes, built with `src/lib/seo.ts` (`faqPage`, `article`, `softwareApplication`). `Seo.astro` adds
  `Organization` and `WebSite`; do not repeat them. If you render `faqPage(faqs)`, render `<Faqs faqs={faqs} />`
  too (or `QuestionsList`, which writes no JSON-LD of its own), so the structured data matches what readers see.
- Never write a host into a link or JSON-LD: build absolute URLs with `absoluteUrl(path, Astro.site)` (`SITE_URL`).
- One `h1`, then `h2` and `h3` in order. No kicker above a heading, and no gradient, middle dot, numbered marker or
  fade-up (design system M1 to M12; `public-style.test.ts` fails on them). Sections get `aria-labelledby`.
- Tokens only (`bg-page`, `bg-background`, `text-foreground`, `text-display`, `text-section`). No hex, no pixel size.
- Every number has a source the page links (`SourcedNumber`), taken from `features.ts` or `docs/research/pain-points.md`.
- No client JavaScript. The only `<script>` is the JSON-LD from `Seo.astro` (plus the opt-in analytics in
  `Analytics.astro`, off by default; see `.claude/rules/astro-public.md`).
- Claims about the product follow the non-negotiables and describe the product as `docs/architecture` designs it.
  Claims about law cite `docs/research/legal.md` or a primary source and carry `NotLegalAdvice`. No statistic
  without a linked source (`docs/design/design-system.md`, Voice).

## 3. Discovery

- The sitemap, the Open Graph image (`/og/<path>.png`) and the `llms.txt` line all come from the page registry and
  the collections through `src/lib/site-index.ts`. Nothing to edit.
- Add the page to `mainNav` or `footerNav` in `src/lib/pages.ts` if it is a top-level section.
- If it changes what ScreenHire does or its principles, update the intro in `src/pages/llms.txt.ts`.

## 4. Checks

```
pnpm --filter @screenhire/marketing check
pnpm --filter @screenhire/marketing test
pnpm --filter @screenhire/marketing build
pnpm --filter @screenhire/marketing lighthouse /<path>/
```

`build` ends with `check-dist`, which fails on more than 15 KB of JS on any page or a broken internal link.
`lighthouse` fails on mobile LCP of 1.8 s or more, or CLS of 0.05 or more. In Git Bash, prefix it with
`MSYS_NO_PATHCONV=1`. It may first wait for another task's run to finish. CI's report-only `lighthouse` job runs it on
every page for a pull request that changes the marketing build.

## 5. Tests

Add the page's path and its expected schema.org types to `IndexablePages` in
`tests/ScreenHire.E2E/MarketingTests.cs`. The sitemap test fails until you do, and the theory then checks the title,
canonical, hreflang, Open Graph image, JSON-LD, the absence of scripts and axe. Rebuild, then run
`dotnet test --project tests/ScreenHire.E2E --filter-class "*MarketingTests"`. Assert page-specific content in its
own test if the page has any.
