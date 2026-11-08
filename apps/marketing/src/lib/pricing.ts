import { catalogPlans, trial, type CataloguePlan } from '@screenhire/plans';
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

/** The plan the free trial starts on, by name: the catalogue's trial plan (packages/plans/trial.json, which billing reads too). */
export const trialPlan = catalogPlans.find((plan) => plan.key === trial.plan)?.name ?? '';

/** The label of a call to action that starts the trial, with its length when the catalogue gives one. */
export function trialLabel(days: number = trial.days): string {
  return days > 0 ? `Start your free ${days}-day trial` : 'Start free trial';
}

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

/** A plan's monthly price in its own currency ("$79", "€149.50"). */
export function formatPrice(plan: Plan): string {
  const amount = Number(plan.monthlyPrice);
  return new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency: plan.priceCurrency,
    minimumFractionDigits: Number.isInteger(amount) ? 0 : 2,
  }).format(amount);
}

/** The lowest set price, for "From $X a month", or an empty string while no plan has a price (a caller then leaves the line out). */
export function startingPrice(allPlans: Plan[]): string {
  const priced = allPlans.filter(hasPrice);
  if (priced.length === 0) return '';
  const lowest = priced.reduce((low, plan) => (Number(plan.monthlyPrice) < Number(low.monthlyPrice) ? plan : low));
  return formatPrice(lowest);
}
