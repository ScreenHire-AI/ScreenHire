import type { PricedOffer } from './seo';

// Prices are the owner's decision. Until a plan has a number, the page shows the placeholder and the plan stays out of
// the SoftwareApplication offers in JSON-LD.
export const PRICE_PLACEHOLDER = '[YOUR PRICE]';

export interface Plan {
  name: string;
  /** A number such as `"99"`, or PRICE_PLACEHOLDER. */
  monthlyPrice: string;
  priceCurrency: string;
  activeJobs: string;
  summary: string;
  highlighted?: boolean;
}

export const plans: Plan[] = [
  {
    name: 'Starter',
    monthlyPrice: PRICE_PLACEHOLDER,
    priceCurrency: 'USD',
    activeJobs: '[YOUR LIMIT] active jobs',
    summary: 'For a team hiring for a few roles at a time.',
  },
  {
    name: 'Growth',
    monthlyPrice: PRICE_PLACEHOLDER,
    priceCurrency: 'USD',
    activeJobs: '[YOUR LIMIT] active jobs',
    summary: 'For a team with a steady flow of open roles.',
    highlighted: true,
  },
  {
    name: 'Unlimited',
    monthlyPrice: PRICE_PLACEHOLDER,
    priceCurrency: 'USD',
    activeJobs: 'Unlimited active jobs',
    summary: 'For a team that is always hiring.',
  },
];

/** What every plan includes. Each line describes the product as designed (docs/architecture). */
export const includedInEveryPlan = [
  'Unlimited users',
  'Applicant integrity checks with evidence, counter-evidence and a suggested next step',
  'Careers page and posting to Google for Jobs',
  'Hiring workflows built from reusable templates',
  'The public API',
];

export function hasPrice(plan: Plan): boolean {
  return /^\d+(\.\d{1,2})?$/.test(plan.monthlyPrice);
}

export function pricedOffers(allPlans: Plan[]): PricedOffer[] {
  return allPlans.filter(hasPrice).map((plan) => ({ name: plan.name, price: plan.monthlyPrice, priceCurrency: plan.priceCurrency }));
}

/** The lowest set price, for "From $X a month", or the placeholder while none is set. */
export function startingPrice(allPlans: Plan[]): string {
  const priced = allPlans.filter(hasPrice).map((plan) => Number(plan.monthlyPrice));
  return priced.length === 0 ? PRICE_PLACEHOLDER : `$${Math.min(...priced)}`;
}
