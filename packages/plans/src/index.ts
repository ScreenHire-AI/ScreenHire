import catalogue from '../plans.json';
import aiDefaults from '../ai.json';
import trialDefaults from '../trial.json';

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

/** The free trial: how many days it lasts (0: no trial) and the key of the plan whose limit applies during it. */
export interface Trial {
  days: number;
  plan: string;
}

/** The trial a new organization gets. The API reads the same file as its default; `ScreenHire:Billing:Trial:*` overrides it there. */
export const trial: Trial = trialDefaults;

/** What AI use on ScreenHire's key costs besides the provider's price: `margin` is the share added (0.2 is 20% on top). */
export interface AiPricing {
  margin: number;
}

/** The margin an organization pays on the built-in AI key. The API reads the same file as its default; `ScreenHire:Billing:Ai:Margin` overrides it there. */
export const ai: AiPricing = aiDefaults;

/** The margin as a whole percentage for copy: 0.2 is 20. */
export const aiMarginPercent: number = Math.round(aiDefaults.margin * 100);
