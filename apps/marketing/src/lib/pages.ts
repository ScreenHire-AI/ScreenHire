// Title, description and path of every hand-written marketing page. The pages, their Open Graph images and llms.txt
// all read from here, so the three cannot drift. Content-collection pages (laws, blog, comparisons) carry the same
// fields in their frontmatter.

export interface PageMeta {
  path: string;
  /** Unique, under about 60 characters. */
  title: string;
  /** One or two sentences, under about 160 characters. */
  description: string;
  /** Short label for navigation and llms.txt. */
  label: string;
}

export const pages = {
  home: {
    path: '/',
    label: 'Home',
    title: "ScreenHire: know who's really applying",
    description:
      'An applicant tracking system for small teams that spots bots, impersonators and copy-paste applications, and explains every flag in plain language.',
  },
  product: {
    path: '/product/',
    label: 'Product',
    title: 'Product: hiring with applicant verification | ScreenHire',
    description:
      'Jobs, hiring workflows, one-click posting and an applicant verification layer that shows evidence and a next step. A person always decides.',
  },
  detection: {
    path: '/how-detection-works/',
    label: 'How detection works',
    title: 'How applicant fraud detection works | ScreenHire',
    description:
      'What ScreenHire looks at, what it never does, and how it stays fair to candidates: evidence instead of verdicts, notice before collection.',
  },
  pricing: {
    path: '/pricing/',
    label: 'Pricing',
    title: 'Pricing for small hiring teams | ScreenHire',
    description:
      'Simple monthly plans with unlimited users and applicant verification included. No credit card to start.',
  },
  laws: {
    path: '/hiring-ai-laws/',
    label: 'Hiring-AI law tracker',
    title: 'Hiring-AI law tracker: EU, UK and US rules | ScreenHire',
    description:
      'Plain-language summaries of the laws that govern AI and automated tools in hiring, with dates, sources and when each was last reviewed.',
  },
  compare: {
    path: '/compare/',
    label: 'Compare',
    title: 'Compare ScreenHire with other applicant tracking systems',
    description: 'Factual, sourced comparisons between ScreenHire and other applicant tracking systems for small teams.',
  },
  blog: {
    path: '/blog/',
    label: 'Blog',
    title: 'Blog: hiring, applicant fraud and fairness | ScreenHire',
    description: 'Notes from the ScreenHire team on applicant fraud, AI in hiring, and running a fair process with a small team.',
  },
} as const satisfies Record<string, PageMeta>;

export const notFound = {
  title: 'Page not found | ScreenHire',
  description: 'The page you were looking for does not exist or has moved.',
} as const;

/** Main navigation, in order. */
export const mainNav: PageMeta[] = [pages.product, pages.detection, pages.pricing, pages.laws, pages.blog];

/** Footer navigation, in order. */
export const footerNav: PageMeta[] = [
  pages.product,
  pages.detection,
  pages.pricing,
  pages.laws,
  pages.compare,
  pages.blog,
];
