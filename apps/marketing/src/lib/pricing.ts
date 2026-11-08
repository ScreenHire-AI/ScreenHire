import { catalogPlans, type CataloguePlan } from '@screenhire/plans';
import type { PricedOffer } from './seo';

// Prices and limits live in the plan catalogue (packages/plans), which the API reads too. They are working values the
// owner confirms before launch (docs/architecture/billing.md). A plan the catalogue gives no price shows its name and
// limit and no amount, and stays out of the SoftwareApplication offers in JSON-LD. No placeholder text reaches a page.

export interface Plan {
  name: string;
  /** A number such as `"99"`, or an empty string while the catalogue has no price for the plan. */
  monthlyPrice: string;
  priceCurrency: string;
  /** The limit in words, such as "3 open jobs"; empty while the catalogue sets none (an unset limit limits nothing). */
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

/**
 * The plan the free trial starts on. It is Growth because billing's `Trial:Plan` defaults to it (ADR-0029); the catalogue
 * does not hold the trial, so the site names the plan and never a trial length.
 */
export const trialPlan = 'Growth';

function priceText(monthlyPrice: number | null): string {
  if (monthlyPrice === null) return '';
  return Number.isInteger(monthlyPrice) ? String(monthlyPrice) : monthlyPrice.toFixed(2);
}

function activeJobsText(plan: CataloguePlan): string {
  if (plan.unlimited) return 'Any number of open jobs';
  if (plan.activeJobs === null) return '';
  // A no-break space keeps the number with its unit (design system W11).
  return plan.activeJobs === 1 ? '1\u00A0open job' : `${plan.activeJobs}\u00A0open jobs`;
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

export function hasPrice(plan: Plan): boolean {
  return /^\d+(\.\d{1,2})?$/.test(plan.monthlyPrice);
}

export function pricedOffers(allPlans: Plan[]): PricedOffer[] {
  return allPlans.filter(hasPrice).map((plan) => ({ name: plan.name, price: plan.monthlyPrice, priceCurrency: plan.priceCurrency }));
}

/** The lowest set price, for "From $X a month", or an empty string while no plan has a price (a caller then leaves the line out). */
export function startingPrice(allPlans: Plan[]): string {
  const priced = allPlans.filter(hasPrice).map((plan) => Number(plan.monthlyPrice));
  return priced.length === 0 ? '' : `$${Math.min(...priced)}`;
}
