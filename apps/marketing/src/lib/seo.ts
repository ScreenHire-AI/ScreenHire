// schema.org builders and URL helpers for the marketing site. Pure functions, so Vitest covers them without a build.
// Every absolute URL is built from the site URL (SITE_URL via Astro.site), never from a hard-coded host (ADR-0003).

export type JsonLd = Record<string, unknown>;

export interface Crumb {
  name: string;
  /** Site-relative path with a trailing slash, e.g. `/pricing/`. */
  path: string;
}

export interface Faq {
  question: string;
  answer: string;
}

/** One English page serves both markets until en-GB copy exists; keep in sync with astro.config.mjs. */
export const HREFLANGS = ['en-US', 'en-GB', 'x-default'] as const;

export const OG_IMAGE_SIZE = { width: 1200, height: 630 } as const;

export function absoluteUrl(path: string, site: URL): string {
  return new URL(path, site).href;
}

/**
 * The key of a page's Open Graph image in the `og/[...route]` endpoint: `/` is `index`, `/hiring-ai-laws/eu-ai-act/`
 * is `hiring-ai-laws/eu-ai-act`. The image is served at `/og/<key>.png`.
 */
export function ogImageKey(pathname: string): string {
  const key = pathname.replace(/^\/+|\/+$/g, '');
  return key === '' ? 'index' : key;
}

export function ogImagePath(pathname: string): string {
  return `/og/${ogImageKey(pathname)}.png`;
}

export function organizationId(site: URL): string {
  return absoluteUrl('/#organization', site);
}

/** Organization and WebSite: on every page, so other nodes can reference them by `@id`. */
export function siteGraph(site: URL): JsonLd[] {
  return [
    { '@type': 'Organization', '@id': organizationId(site), name: 'ScreenHire', url: site.href },
    {
      '@type': 'WebSite',
      '@id': absoluteUrl('/#website', site),
      name: 'ScreenHire',
      url: site.href,
      inLanguage: 'en',
      publisher: { '@id': organizationId(site) },
    },
  ];
}

/** Home is always the first crumb; pass the rest in order, ending with the current page. */
export function breadcrumbList(crumbs: Crumb[], site: URL): JsonLd {
  const all = [{ name: 'Home', path: '/' }, ...crumbs];
  return {
    '@type': 'BreadcrumbList',
    itemListElement: all.map((crumb, index) => ({
      '@type': 'ListItem',
      position: index + 1,
      name: crumb.name,
      item: absoluteUrl(crumb.path, site),
    })),
  };
}

export function faqPage(faqs: Faq[]): JsonLd {
  if (faqs.length === 0) {
    throw new Error('An FAQPage needs at least one question.');
  }

  return {
    '@type': 'FAQPage',
    mainEntity: faqs.map((faq) => ({
      '@type': 'Question',
      name: faq.question,
      acceptedAnswer: { '@type': 'Answer', text: faq.answer },
    })),
  };
}

export interface ArticleInput {
  type: 'Article' | 'BlogPosting';
  headline: string;
  description: string;
  path: string;
  /** ISO dates (`YYYY-MM-DD`). */
  datePublished: string;
  dateModified?: string;
}

export function article(input: ArticleInput, site: URL): JsonLd {
  const url = absoluteUrl(input.path, site);
  return {
    '@type': input.type,
    headline: input.headline,
    description: input.description,
    url,
    mainEntityOfPage: url,
    image: absoluteUrl(ogImagePath(input.path), site),
    datePublished: input.datePublished,
    dateModified: input.dateModified ?? input.datePublished,
    inLanguage: 'en',
    author: { '@id': organizationId(site) },
    publisher: { '@id': organizationId(site) },
  };
}

export interface PricedOffer {
  name: string;
  /** A numeric price. Plans whose price is still a placeholder are left out of structured data. */
  price: string;
  priceCurrency: string;
}

export function softwareApplication(description: string, site: URL, offers: PricedOffer[] = []): JsonLd {
  return {
    '@type': 'SoftwareApplication',
    name: 'ScreenHire',
    applicationCategory: 'BusinessApplication',
    applicationSubCategory: 'Applicant tracking system',
    operatingSystem: 'Web',
    url: site.href,
    description,
    publisher: { '@id': organizationId(site) },
    // An Offer without a numeric price is invalid structured data, so offers appear only once prices are set.
    ...(offers.length > 0 && {
      offers: offers.map((offer) => ({ '@type': 'Offer', name: offer.name, price: offer.price, priceCurrency: offer.priceCurrency })),
    }),
  };
}

/**
 * Serializes a JSON-LD graph for an inline `<script>`. JSON.stringify leaves `<` alone, so a `</script>` in any
 * frontmatter string would end the element early; `<` is the same character to a JSON parser.
 */
export function jsonLdScript(graph: JsonLd): string {
  return JSON.stringify(graph).replace(/</g, '\\u003c');
}

/** Formats an ISO date (`YYYY-MM-DD`) for display. Calendar dates have no time zone, so format them in UTC. */
export function formatDate(isoDate: string): string {
  return new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'long', year: 'numeric', timeZone: 'UTC' }).format(
    new Date(`${isoDate}T00:00:00Z`),
  );
}
