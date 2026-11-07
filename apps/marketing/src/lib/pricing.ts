import { catalogPlans, type CataloguePlan } from '@screenhire/plans';
import type { PricedOffer } from './seo';

// Prices and limits are the owner's decision and live in the plan catalogue (packages/plans), which the API reads too.
// Until a plan has a number, the page shows the placeholder and the plan stays out of the SoftwareApplication offers
// in JSON-LD.
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

// The words and emphasis are the site's own; the catalogue holds only the numbers. Keyed by the catalogue's plan key.
const copyByKey: Record<string, { summary: string; highlighted?: boolean }> = {
  starter: { summary: 'For a team hiring for a few roles at a time.' },
  growth: { summary: 'For a team with a steady flow of open roles.', highlighted: true },
  unlimited: { summary: 'For a team that is always hiring.' },
};

function priceText(monthlyPrice: number | null): string {
  if (monthlyPrice === null) return PRICE_PLACEHOLDER;
  return Number.isInteger(monthlyPrice) ? String(monthlyPrice) : monthlyPrice.toFixed(2);
}

function activeJobsText(plan: CataloguePlan): string {
  if (plan.unlimited) return 'Unlimited active jobs';
  if (plan.activeJobs === null) return '[YOUR LIMIT] active jobs';
  return plan.activeJobs === 1 ? '1 active job' : `${plan.activeJobs} active jobs`;
}

/** The page's plans from a catalogue, in the catalogue's order. */
export function buildPlans(catalogue: CataloguePlan[]): Plan[] {
  return catalogue.map((plan) => {
    const copy = copyByKey[plan.key];
    return {
      name: plan.name,
      monthlyPrice: priceText(plan.monthlyPrice),
      priceCurrency: plan.currency,
      activeJobs: activeJobsText(plan),
      summary: copy?.summary ?? '',
      ...(copy?.highlighted ? { highlighted: true } : {}),
    };
  });
}

export const plans: Plan[] = buildPlans(catalogPlans);

/** What every plan includes. Each line describes the product as designed (docs/architecture). */
export const includedInEveryPlan = [
  'Unlimited users',
  'Applicant verification with evidence, counter-evidence and a suggested next step',
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
