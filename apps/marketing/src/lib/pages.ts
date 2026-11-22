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
    title: 'ScreenHire: interview real people, not bots',
    description:
      'The hiring tool for small teams. It checks every applicant, keeps your team and candidates up to date, and automates the busywork. One price.',
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
      "The team's activity in one feed, email kept on the application, notes, tasks and reports, and a page of their own for every candidate.",
  },
  automateAnything: {
    path: '/product/automate-anything/',
    label: 'Automate anything',
    title: 'Hiring automations on every plan | ScreenHire',
    description:
      'Automate a reminder or a whole process, book interviews three ways, try it on a made-up candidate first, and know a check never rejects anyone.',
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
      'Your stages, forms, scorecards, emails and colours, and who sees which jobs. Start from a hiring template or build your own.',
  },
  careersSite: {
    path: '/product/careers-site/',
    label: 'Careers site',
    title: 'Careers site and job posting | ScreenHire',
    description:
      'A branded careers site, the apply form on your own site, Google for Jobs, job boards and postings you make by hand, and a notice before candidates apply.',
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
  developers: {
    path: '/developers/',
    label: 'Developers',
    title: 'Developers: the ScreenHire API, webhooks and MCP',
    description: 'Build on ScreenHire: call the API with a key, get signed webhooks when something happens, and connect an AI assistant through MCP.',
  },
  developerAuthentication: {
    path: '/developers/authentication/',
    label: 'Authentication',
    title: 'Authentication: API keys, OAuth and scopes | ScreenHire',
    description: 'How an API key or an AI assistant signs in to ScreenHire, what the Read, Write and Verification read scopes allow, and the rate limit.',
  },
  developerApi: {
    path: '/developers/api/',
    label: 'API reference',
    title: 'API reference: every route a key can call | ScreenHire',
    description: 'Every route of the ScreenHire API an organization key can call, by area, with errors, paging and versioning, and the full reference.',
  },
  developerWebhooks: {
    path: '/developers/webhooks/',
    label: 'Webhooks',
    title: 'Webhooks: events, signatures and retries | ScreenHire',
    description: 'Every webhook event ScreenHire sends with a sample payload, how to check the signature in Node, Python and C#, and how retries and replays work.',
  },
  developerMcp: {
    path: '/developers/mcp/',
    label: 'MCP for AI assistants',
    title: 'MCP server: connect an AI assistant | ScreenHire',
    description: 'Connect Claude or another AI assistant to ScreenHire through MCP: the tools it gets, the access each needs, and what it asks you before it acts.',
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

/** The developer docs, in reading order. Each of their pages lists them beside its content. */
export const developerNav: PageMeta[] = [
  pages.developers,
  pages.developerAuthentication,
  pages.developerApi,
  pages.developerWebhooks,
  pages.developerMcp,
];

/** Footer navigation, in order. */
export const footerNav: PageMeta[] = [
  ...productNav,
  pages.pricing,
  pages.laws,
  pages.compare,
  pages.blog,
  pages.developers,
];
