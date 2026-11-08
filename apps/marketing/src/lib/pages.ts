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
    title: 'Product: hiring tools for small teams | ScreenHire',
    description:
      'Checks on every applicant, a pipeline your team and candidates can follow, automations, integrations and a setup that bends to how you hire.',
  },
  realPeople: {
    path: '/product/real-people/',
    label: 'How checks work',
    title: 'How checks work: real applicants, not bots | ScreenHire',
    description:
      'Every application is checked and shown with its evidence, what speaks for the candidate and a next step. A person decides. Nothing is ever rejected on a check.',
  },
  neverGuessing: {
    path: '/product/never-guessing/',
    label: 'Never guessing',
    title: 'Know where every candidate stands | ScreenHire',
    description:
      "The pipeline, each candidate's activity, email kept on the application, notes, tasks and reports, and a page of their own for every candidate.",
  },
  automateAnything: {
    path: '/product/automate-anything/',
    label: 'Automate anything',
    title: 'Hiring automations on every plan | ScreenHire',
    description:
      'Automate a reminder or a whole process on any stage, test it on a sample application before it goes live, and know a check never rejects anyone.',
  },
  integrations: {
    path: '/product/integrations/',
    label: 'Integrations',
    title: 'Integrations: email, calendars, Slack and API | ScreenHire',
    description:
      'Email with replies kept on the candidate, Google and Outlook calendars, Slack, the API and webhooks, and AI assistants through MCP. On every plan.',
  },
  builtYourWay: {
    path: '/product/built-your-way/',
    label: 'Built your way',
    title: 'Set up hiring your way: stages, forms, branding | ScreenHire',
    description:
      'Your stages, questionnaires, scorecards, emails and colours, and who sees which jobs. Start from a hiring template or build your own.',
  },
  careersSite: {
    path: '/product/careers-site/',
    label: 'Careers site',
    title: 'Careers site and job posting | ScreenHire',
    description:
      'A branded careers site, the apply form on your own site, Google for Jobs and job boards, and a notice candidates see before they apply.',
  },
  pricing: {
    path: '/pricing/',
    label: 'Pricing',
    title: 'Pricing for small hiring teams | ScreenHire',
    description:
      'One monthly price by how many jobs you have open. Unlimited users, and every feature on every plan, including the applicant checks.',
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

/** The product overview and the page for each selling point, in order. The header and footer list them under Product. */
export const productNav: PageMeta[] = [
  pages.product,
  pages.realPeople,
  pages.neverGuessing,
  pages.automateAnything,
  pages.integrations,
  pages.builtYourWay,
  pages.careersSite,
];

/** Main navigation, in order. The header shows `pages.product` as a menu of `productNav`. */
export const mainNav: PageMeta[] = [pages.product, pages.pricing, pages.laws, pages.blog];

/** Footer navigation, in order. */
export const footerNav: PageMeta[] = [
  ...productNav,
  pages.pricing,
  pages.laws,
  pages.compare,
  pages.blog,
];
