// What other applicant trackers charge and sell, for the compare index. Each fact is what the vendor's own page says, with
// that page and the day it was read (docs/research/pain-points.md holds the check of each). Word a fact as the page words
// it. Add a vendor only with a page that says it today; never a claim from memory, from a review site or about what a
// product lacks.
import type { Source } from './features';

export interface MarketFact {
  vendor: string;
  /** What the vendor's page says. */
  fact: string;
  source: Source;
  /** The day the page was read, `YYYY-MM-DD`. */
  asOf: string;
}

export interface MarketPain {
  id: string;
  /** The pain, in the buyer's words. */
  pain: string;
  facts: MarketFact[];
  /** What ScreenHire does instead (docs/design/marketing-site.md, "Under all five points"). */
  answer: string;
}

export const marketPains: MarketPain[] = [
  {
    id: 'seat-and-size',
    pain: 'Priced by the seat, or for bigger teams',
    facts: [
      {
        vendor: 'Ashby',
        fact: 'From $400 a month, for companies of up to 100 employees. Ashby offers 10% off for a yearly commitment and does not say which term the $400 is.',
        source: { label: 'Ashby pricing page', href: 'https://www.ashbyhq.com/pricing' },
        asOf: '2026-11-08',
      },
      {
        vendor: 'Workable',
        fact: 'From $299 a month, shown as $3,588 a year, for companies of 1 to 20 employees. Paying yearly saves 20%, so a month at a time costs more.',
        source: { label: 'Workable pricing page', href: 'https://www.workable.com/pricing' },
        asOf: '2026-11-08',
      },
      {
        vendor: 'Manatal',
        fact: 'The Professional plan is $15 per user per month, billed annually.',
        source: { label: 'Manatal pricing page', href: 'https://www.manatal.com/pricing' },
        asOf: '2026-11-08',
      },
    ],
    answer: 'Unlimited users on every plan. The price is set by how many jobs are open at once, and a closed job costs nothing.',
  },
  {
    id: 'fraud-sold-apart',
    pain: 'Fraud checks sold apart, or on a higher tier',
    facts: [
      {
        vendor: 'Gem',
        fact: 'Sells fraud detection as checks the customer must purchase.',
        source: { label: 'Gem help centre, fraud detection agent overview', href: 'https://help.gem.com/databases/gem-help-center/fraud-detection-agent-overview' },
        asOf: '2026-11-08',
      },
      {
        vendor: 'Greenhouse',
        fact: 'Identity verification is available only on the Plus and Pro tiers.',
        source: { label: 'Greenhouse support, identity verification overview', href: 'https://support.greenhouse.io/hc/en-us/articles/40966215931291' },
        asOf: '2026-11-08',
      },
    ],
    answer: 'A check on every application, on every plan.',
  },
  {
    id: 'ai-sold-apart',
    pain: 'AI sold as an add-on',
    facts: [
      {
        vendor: 'GoHire',
        fact: 'Sells its AI as the Plus+ add-on, at £40 or $50 a month.',
        source: { label: 'GoHire help centre, the Plus plan', href: 'https://help.gohire.io/en/articles/12358433-boost-your-hiring-power-with-the-plus-plan' },
        asOf: '2026-11-08',
      },
    ],
    answer: 'The AI consistency check is on every plan, with your own key and model if you prefer. Nothing is metered.',
  },
];
