// What the marketing site says it sells, in one place. Home, product and pricing all read this module, and
// docs/design/marketing-site.md is the prose version: change both together. Every claim names a feature that is built
// today (docs/status.md, Built), every number has a source the page links to (docs/research/pain-points.md and
// competitors.md hold the sources and the date each was read), and nothing here names a reason code, a rule or a score.

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
  id: 'real-people' | 'never-guessing' | 'automate-anything' | 'integrations' | 'built-your-way';
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
} satisfies Record<string, Source>;

export const sellingPoints: SellingPointData[] = [
  {
    id: 'real-people',
    title: "Know it's a real person before you book the interview.",
    pain: 'Auto-apply tools and fake identities fill your inbox, and every one of them costs your team time.',
    promise: 'ScreenHire checks every application as it arrives and shows you what does not add up, what speaks for the candidate, and what to do next.',
    proof: [
      'A check on every application',
      'A level for every applicant, with the evidence behind it and what speaks for them',
      'A suggested next step',
      'A person on your team decides. Nothing is rejected on a check.',
    ],
    figures: [
      { value: '291', claim: "applications for every hire, on average, across Ashby's customers.", source: sources.ashby },
      { value: '31%', claim: 'of US managers involved in hiring say they interviewed a candidate who turned out to be using a fake identity.', source: sources.checkr },
      { value: '1 in 4', claim: 'candidate profiles worldwide could be fake by 2028, Gartner predicts.', source: sources.gartner },
    ],
    pagePath: '/product/#real-people',
  },
  {
    id: 'never-guessing',
    title: 'Nobody has to ask where things stand.',
    pain: 'Nobody knows who spoke to a candidate last, and candidates are left without an answer.',
    promise: 'Your team always knows where each candidate is and what was said last, and so does the candidate.',
    proof: [
      'The pipeline, and on each candidate their activity and when they were last contacted',
      'Email you send and the replies you get, kept on the application',
      'Notes and tasks on the candidate, with mentions',
      'The candidate\'s own page: four steps (Applied, In review, Interview, Offer) and your messages',
      'Reports on your funnel, time to hire and sources',
    ],
    figures: [],
    pagePath: '/product/#never-guessing',
  },
  {
    id: 'automate-anything',
    title: 'If your team does it by hand, ScreenHire can do it for you.',
    pain: 'The same follow-ups by hand, or automation kept for the top plan.',
    promise: 'Whatever you do the same way every time, ScreenHire can run it, from one reminder to a whole process.',
    proof: [
      'Automations on every stage and on the whole workflow, on every plan',
      'Triggers such as a candidate reaching a stage, a time limit passing, a scorecard coming in or an interview being booked',
      'Actions such as emailing the candidate, moving them, asking a teammate to review, reminding the team, sending follow-up questions or offering interview times',
      'Test an automation on a sample application before it goes live',
      'Guardrails: a check never rejects anyone',
    ],
    figures: [],
    pagePath: '/product/#automate-anything',
  },
  {
    id: 'integrations',
    title: 'Works with the tools you already use.',
    pain: 'Tools that do not talk to each other, or no API at all.',
    promise: 'No copying between tabs. If you want to build on ScreenHire, the API and webhooks are on every plan.',
    proof: [
      'Email, with replies kept on the candidate',
      'Google and Outlook calendars',
      'Slack',
      'The API and webhooks',
      'AI assistants through MCP',
    ],
    figures: [],
    pagePath: '/product/#integrations',
  },
  {
    id: 'built-your-way',
    title: 'Set it up the way your team hires.',
    pain: 'Rigid software built for big recruiting teams.',
    promise: 'ScreenHire bends to yours: your stages, your questions, your scorecards, your emails, your colours, and who sees which jobs.',
    proof: [
      'Stages and hiring templates, ours or your own',
      'Questionnaires and scorecards',
      'Email templates',
      'Your branding and careers site',
      'Roles and access to each job',
      'Your own AI key and model for the consistency check',
    ],
    figures: [],
    pagePath: '/product/#built-your-way',
  },
];

/** The integrations as a plain list of what each does (the integrations section). */
export const integrations: { name: string; does: string }[] = [
  { name: 'Email', does: "Send from ScreenHire. Replies land on the candidate's record." },
  { name: 'Calendars', does: 'Google and Outlook. Candidates pick a time your panel is free.' },
  { name: 'Slack', does: 'New applicants in a channel, mentions and tasks to the right person.' },
  { name: 'API and webhooks', does: 'Read and change jobs, applications and candidates, and get a signed webhook when something happens.' },
  { name: 'AI assistants', does: 'Ask an assistant such as Claude about your pipeline through MCP.' },
];

/** An automation written out as When, If, Then, then three examples from one step to a whole process. */
export const automation = {
  sentence:
    'When a candidate reaches Interview, if they said yes to right to work, offer them interview times and ask Jordan to review.',
  examples: [
    { scope: 'One step', text: 'Thank everyone who applies, in your own words.' },
    { scope: 'A few steps', text: 'Remind the team when a candidate has waited three days in Screening.' },
    {
      scope: 'Your whole process',
      text: 'Send follow-up questions, move people who meet your must-haves to Screening, offer interview times, and ask your reviewers for a scorecard.',
    },
  ],
};

/** What the dashboard calls each level, with the status tone that draws it. The site shows nothing else about a check. */
export const levels = {
  noConcerns: { label: 'No concerns', status: 'done' },
  reviewSuggested: { label: 'Review suggested', status: 'review' },
  verificationSuggested: { label: 'Verification suggested', status: 'verification' },
  someSignsOfAiHelp: { label: 'Some signs of AI help', status: 'ai' },
} as const;

export type LevelKey = keyof typeof levels;

/** The home hero. */
export const hero = {
  headline: 'Interview real people, not bots.',
  subline:
    'ScreenHire is the hiring tool for small teams. It checks every applicant, keeps your team and your candidates up to date, and does the busywork for you. One price, everything included.',
};

/** What every plan includes, grouped by the five selling points and the candidate's side. Each line is a built feature (docs/status.md). */
export const includedByTopic: { id: string; title: string; items: string[] }[] = [
  {
    id: 'real-people',
    title: 'Real people',
    items: [
      'A check on every application',
      'A level for every applicant, with the evidence, what speaks for them and a suggested next step',
      'The AI consistency check, with your own key and model if you prefer',
    ],
  },
  {
    id: 'never-guessing',
    title: 'Never guessing',
    items: [
      "The pipeline, with each candidate's activity and when they were last contacted",
      'Email you send and the replies you get, kept on the application',
      'Notes and tasks with mentions, and reports on your funnel',
      'A page of their own for every candidate',
    ],
  },
  {
    id: 'automate-anything',
    title: 'Automate anything',
    items: [
      'Automations on every stage and on the whole workflow, tested on a sample application before they go live',
      'Follow-up questions and interview scheduling',
    ],
  },
  {
    id: 'integrations',
    title: 'Integrations',
    items: ['Google and Outlook calendars, and Slack', 'The API and webhooks', 'AI assistants through MCP'],
  },
  {
    id: 'built-your-way',
    title: 'Built your way',
    items: ['Hiring templates, questionnaires and scorecards', 'Email templates and your branding', 'Roles, and access to each job'],
  },
  {
    id: 'careers-site',
    title: 'Careers site and job posting',
    items: [
      'A careers site for your company, with the notice before any candidate applies',
      'The apply form on your own site, from one line of embed code',
      'Posting to Google for Jobs and job-board feeds, and taking a job down everywhere in one click',
    ],
  },
];

/**
 * What you will not pay extra for, and what one page of the market says today. `elsewhere` names the page and says only what
 * it says; each source was read on the date in its label (docs/research/competitors.md, "Pricing pages re-read on
 * 2026-11-08"). Word these as what the page lists, never as what a vendor lacks.
 */
export const pricingNotes: { ours: string; elsewhere: string; source: Source }[] = [
  {
    ours: 'Another seat. Your whole team is included.',
    elsewhere: 'Manatal lists $15\u00A0per user per month on its Professional plan, billed yearly.',
    source: { label: 'Manatal pricing page, read 8 November 2026', href: 'https://www.manatal.com/pricing' },
  },
  {
    ours: 'Fraud and bot checks, or the AI that helps with them.',
    elsewhere: 'Gem sells fraud detection as checks you purchase separately.',
    source: {
      label: 'Gem help center, read 8 November 2026',
      href: 'https://help.gem.com/databases/gem-help-center/fraud-detection-agent-overview',
    },
  },
  {
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
