// What the marketing site says it sells, in one place. Home, product and pricing all read this module, and
// docs/design/marketing-site.md is the prose version: change both together. Every claim names a feature that is built
// today (docs/status.md, Built), every number has a source the page links to (docs/research/pain-points.md and
// competitors.md hold the sources and the date each was read), and nothing here says how a check works or gives a score
// (public-copy.test.ts guards it).

import { aiMarginPercent } from '@screenhire/plans';

/**
 * What the AI consistency check costs beyond the plan, in the words every page uses. The plan includes ScreenHire's checks. The AI
 * check calls a model, and use of ScreenHire's own AI key is billed at the provider's price plus a margin, which is the share in
 * packages/plans/ai.json that the product reads too (docs/architecture/billing.md, "AI usage"). With the customer's own AI key
 * ScreenHire charges nothing for it. Never say the AI is free, and never retype the margin: it comes from the catalogue.
 */
const billed = `Use of our AI key is billed at the provider's price plus ${aiMarginPercent}%.`;
const ownKey = 'Bring your own AI key and we charge nothing for it.';

export const aiCost = {
  /** The margin as a whole percentage, from the catalogue. */
  marginPercent: aiMarginPercent,
  billed,
  ownKey,
  /** Both sentences, for a place with room for them. */
  both: `${billed} ${ownKey}`,
};

export interface Source {
  label: string;
  href: string;
}

/** A number, what it says and where it comes from. */
export interface SourcedFigure {
  value: string;
  claim: string;
  source: Source;
}

export interface SellingPointData {
  id: 'real-people' | 'never-guessing' | 'automate-anything' | 'integrations' | 'built-your-way' | 'careers-site';
  /** The section heading, in the buyer's words. */
  title: string;
  /** The buyer's pain. */
  pain: string;
  /** Our promise. */
  promise: string;
  /** What backs the promise, each a feature that is built. */
  proof: string[];
  /** Sourced numbers for the pain. Optional: a pain without a verified number has none. */
  figures: SourcedFigure[];
  /** The page that tells it in full. Until a page of its own exists, an anchor on the product page. */
  pagePath: string;
}

// The Ashby URL still has "2023" in its slug, but the page is the 2026 Talent Trends Report (docs/research/pain-points.md).
export const sources = {
  ashby: {
    label: 'Ashby, Talent Trends Report, 2026',
    href: 'https://www.ashbyhq.com/talent-trends-report/reports/2023-recruiter-productivity-trends-report',
  },
  checkr: {
    label: 'Checkr, Hiring Hoax survey, 2025',
    href: 'https://checkr.com/resources/articles/hiring-hoax-manager-survey-2025',
  },
  gartner: {
    label: 'Gartner forecast, as reported by HR Dive, 2025',
    href: 'https://www.hrdive.com/news/fake-job-candidates-ai/757126/',
  },
  monster: {
    label: 'Monster poll of more than 1,400 US workers, November 2024',
    href: 'https://www.monster.com/career-advice/research/job-search-fatigue-poll-2024',
  },
} satisfies Record<string, Source>;

export const sellingPoints: SellingPointData[] = [
  {
    id: 'real-people',
    title: "Know it's a real person before you book the interview.",
    pain: 'Auto-apply tools and fake identities fill your inbox. Each one costs your team time it never gets back.',
    promise: 'Every application is checked as it arrives. You see what does not add up, what speaks for the candidate, and what to do next.',
    proof: [
      'A check on every application, on every plan',
      'Clear, Review or Verify on every applicant, with the evidence behind it',
      'A suggested next step',
      'A person on your team decides. Nothing is rejected on a check.',
    ],
    figures: [
      { value: '291', claim: "applications for every hire, on average, across Ashby's customers.", source: sources.ashby },
      { value: '31%', claim: 'of US managers involved in hiring say they interviewed a candidate who turned out to be using a fake identity.', source: sources.checkr },
      { value: '1 in 4', claim: 'candidate profiles worldwide could be fake by 2028, Gartner predicts.', source: sources.gartner },
    ],
    pagePath: '/product/real-people/',
  },
  {
    id: 'never-guessing',
    title: 'Nobody has to ask where things stand.',
    pain: 'Nobody knows who spoke to a candidate last, and the candidate hears nothing.',
    promise: 'Every change lands in one feed your team reads in a minute. Every candidate gets a page that says where they stand.',
    proof: [
      "The team's activity in one feed, with what changed since you last looked",
      'Email, replies and notes kept on the candidate',
      "The candidate's own page: four steps (Applied, In review, Interview, Offer) and your messages",
      'Reports on your funnel, time to hire and sources',
    ],
    figures: [
      { value: '59%', claim: 'of US workers say they rarely hear back after applying for a job, and 33% say they never do.', source: sources.monster },
    ],
    pagePath: '/product/never-guessing/',
  },
  {
    id: 'automate-anything',
    title: 'If your team does it by hand, ScreenHire can do it for you.',
    pain: 'The same follow-ups by hand, or automation kept for the top plan.',
    promise: 'Whatever you do the same way every time, ScreenHire runs it, from one reminder to a whole hiring process.',
    proof: [
      'Automations on every stage and on the whole workflow, on every plan',
      'Interviews booked three ways: from your times, from the times they are free, or a time you suggest',
      'Try it: walk a made-up candidate through your process, one stage at a time, before anyone applies',
      'Guardrails: a check never rejects anyone',
    ],
    figures: [],
    pagePath: '/product/automate-anything/',
  },
  {
    id: 'integrations',
    title: 'Works with the tools you already use.',
    pain: 'Tools that do not talk to each other, or no API at all.',
    promise: 'No copying between tabs. The API, webhooks and AI assistants are on every plan, and an assistant asks you before it sends anything.',
    proof: [
      'Email, with replies kept on the candidate',
      'Google and Outlook calendars, and Slack',
      'The API and signed webhooks',
      'AI assistants through MCP, which ask before they act',
    ],
    figures: [],
    pagePath: '/product/integrations/',
  },
  {
    id: 'built-your-way',
    title: 'Set it up the way your team hires.',
    pain: 'Rigid software built for big recruiting teams.',
    promise: 'Your stages, your forms, your emails, your colours and your teams. Change any of it later, even on a job that is already open.',
    proof: [
      'Hiring templates, ours or your own',
      'A library of forms and scorecards, used again on every job',
      'Your branding, with any heading font from Google Fonts',
      'Roles, named teams and access to each job',
    ],
    figures: [],
    pagePath: '/product/built-your-way/',
  },
];

/**
 * The candidate's side, told as the same pain, promise and proof. It is not one of the owner's five selling points, so it
 * is not in `sellingPoints`; the product overview and the careers-site page read it.
 */
export const careersSite: SellingPointData = {
  id: 'careers-site',
  title: 'Post once. Take it down once.',
  pain: 'A job goes on your site, then on each board by hand, and coming down means doing it all again.',
  promise: 'Publish to your careers site, Google for Jobs and the job boards in one step, and close it the same way. Anywhere else, copy the text and we track the link.',
  proof: [
    'A careers site with your branding, and the apply form on your own site from one line of embed code',
    'Google for Jobs and job-board feeds, each board showing its own status',
    'Post it yourself on any other board, with a tracked link and the posting in the same list',
    'A short apply form with one progress bar, and a notice before it starts',
  ],
  figures: [],
  pagePath: '/product/careers-site/',
};

/** The integrations as a plain list of what each does (the integrations section). */
export const integrations: { name: string; does: string }[] = [
  { name: 'Email', does: 'Send from ScreenHire. Replies land on the candidate.' },
  { name: 'Calendars', does: 'Google and Outlook. Candidates pick from your times, tell you when they are free, or take a time you suggest.' },
  { name: 'Slack', does: 'New applicants in a channel, mentions and tasks to the right person.' },
  { name: 'API and webhooks', does: 'Read and change jobs, applications, candidates and scorecards, and get a signed webhook when something happens.' },
  { name: 'AI assistants', does: 'Ask an assistant such as Claude about your pipeline through MCP. It asks you before it sends, publishes or rejects.' },
];

/**
 * An automation written out as When, If, Then, three examples from one step to a whole process, and the line on Try it
 * (the workflow's Try it tab, docs/architecture/workflow-engine.md), which walks a made-up candidate through the stages.
 */
export const automation = {
  sentence:
    'When a candidate reaches Interview, if they said yes to right to work, offer them interview times and ask Jordan to review.',
  examples: [
    { scope: 'One step', text: 'Thank everyone who applies, in your own words.' },
    { scope: 'A few steps', text: 'Remind the team when a candidate has waited three days in Screening.' },
    {
      scope: 'Your whole process',
      text: 'Send a form, move people who meet your must-haves to Screening, offer interview times, and ask your reviewers for a scorecard.',
    },
  ],
  tryIt: 'Then press Try it and walk a made-up candidate through every stage, one at a time, before a real one applies.',
};

/**
 * The authenticity levels as the dashboard shows them (design system section 3.2, `verificationStateOf` in the dashboard):
 * one word on screen and the long name for screen readers. Clear is quiet, an icon and a muted word, as a calm state is
 * (T3). The site shows nothing else about a check.
 */
export const levels = {
  clear: { word: 'Clear', label: 'No concerns', status: 'done', quiet: true },
  review: { word: 'Review', label: 'Review suggested', status: 'review', quiet: false },
  verify: { word: 'Verify', label: 'Verification suggested', status: 'verification', quiet: false },
} as const;

export type LevelKey = keyof typeof levels;

/** AI help is a separate assessment and only information, never a level of concern, so it is not in `levels`. */
export const aiHelp = { word: 'Some signs of AI help', label: 'Some signs of AI help', status: 'ai', quiet: false } as const;

/** The home hero. */
export const hero = {
  headline: 'Interview real people, not bots.',
  subline:
    'The hiring tool for small teams. It checks every applicant, keeps your team and your candidates up to date, and does the busywork. One price, everything included.',
};

/** What every plan includes, grouped by the five selling points and the candidate's side. Each line is a built feature (docs/status.md). */
export const includedByTopic: { id: string; title: string; items: string[] }[] = [
  {
    id: 'real-people',
    title: 'Real people',
    items: [
      'A check on every application',
      'Clear, Review or Verify on every applicant, with the evidence, what speaks for them and a suggested next step',
      'A log of every run of the checks, and a switch for each group of checks',
      `The AI consistency check, on our AI key at the provider's price plus ${aiMarginPercent}%, or on your own AI key at no charge from us`,
    ],
  },
  {
    id: 'never-guessing',
    title: 'Never guessing',
    items: [
      "The team's activity in one feed, and each person's own choice of alerts and a daily digest",
      'Email you send and the replies you get, kept on the application',
      'Notes and tasks with mentions, and reports on your funnel',
      'A page of their own for every candidate',
    ],
  },
  {
    id: 'automate-anything',
    title: 'Automate anything',
    items: [
      'Automations on every stage and on the whole workflow',
      'Try it: click through your hiring process before anyone applies',
      "Interview scheduling three ways, within each interviewer's own hours",
    ],
  },
  {
    id: 'integrations',
    title: 'Integrations',
    items: ['Google and Outlook calendars, and Slack', 'The API and signed webhooks', 'AI assistants through MCP, which ask before they act'],
  },
  {
    id: 'built-your-way',
    title: 'Built your way',
    items: [
      'Hiring templates, and a library of forms and scorecards',
      'Email templates and your branding, with any heading font from Google Fonts',
      'Roles, named teams and access to each job',
    ],
  },
  {
    id: 'careers-site',
    title: 'Careers site and job posting',
    items: [
      'A careers site for your company, with the notice before any candidate applies',
      'The apply form on your own site, from one line of embed code',
      'Posting to Google for Jobs and job-board feeds, and taking a job down everywhere in one click',
      'Post it yourself anywhere else, with a tracked link',
    ],
  },
];

/**
 * What you will not pay extra for, and what one page of the market says today. `elsewhere` names the page and says only what
 * it says; each source was read on the date in its label (docs/research/competitors.md, "Pricing pages re-read on
 * 2026-11-08"). Word these as what the page lists, never as what a vendor lacks.
 */
export const pricingNotes: { ours: string; elsewhere: string; source: Source; topic?: SellingPointData['id'] }[] = [
  {
    ours: 'Another seat. Your whole team is included.',
    elsewhere: 'Manatal lists $15\u00A0per user per month on its Professional plan, billed yearly.',
    source: { label: 'Manatal pricing page, read 8 November 2026', href: 'https://www.manatal.com/pricing' },
  },
  {
    topic: 'real-people',
    ours: 'Fraud and bot checks.',
    elsewhere: 'Gem sells fraud detection as checks you purchase separately.',
    source: {
      label: 'Gem help center, read 8 November 2026',
      href: 'https://help.gem.com/databases/gem-help-center/fraud-detection-agent-overview',
    },
  },
  {
    topic: 'automate-anything',
    ours: 'Automation, the API or webhooks.',
    elsewhere: 'GoHire lists Automated Stage Actions on its Pro plan.',
    source: { label: 'GoHire pricing page, read 8 November 2026', href: 'https://gohire.io/pricing' },
  },
  {
    ours: 'A job you have closed. Only open jobs count.',
    elsewhere: 'GoHire counts a closed job as an active one, and says to archive it to stop being charged.',
    source: { label: 'GoHire pricing page, read 8 November 2026', href: 'https://gohire.io/pricing' },
  },
];

/** What the pricing section says you will not pay extra for. */
export const notPaidExtra = pricingNotes.map((note) => note.ours);
