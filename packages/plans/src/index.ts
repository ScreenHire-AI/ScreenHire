import catalogue from '../plans.json';

/** One plan as the catalogue holds it. A null price or limit is not set yet: the owner decides both. */
export interface CataloguePlan {
  key: string;
  name: string;
  order: number;
  monthlyPrice: number | null;
  currency: string;
  /** The most jobs open at once, or null when not set. Ignored when `unlimited`. */
  activeJobs: number | null;
  unlimited: boolean;
  stripePriceId: string | null;
}

type Entry = Omit<CataloguePlan, 'key'>;

/** The plans in display order. The API reads the same file as its defaults; configuration overrides any field there. */
export const catalogPlans: CataloguePlan[] = Object.entries(catalogue as Record<string, Entry>)
  .map(([key, plan]) => ({ ...plan, key }))
  .sort((a, b) => a.order - b.order);
