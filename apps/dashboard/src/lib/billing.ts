import type { BillingAiUsage, BillingOverview, BillingPlan, BillingState } from '@screenhire/api-client';
import { formatCost } from '#lib/ai-usage.js';
import { locales } from '#lib/dates.js';
import type { StatusName } from '#lib/status.js';

/** How each billing state reads as a status badge (design system section 3.2): an icon, a colour and these words. */
export const billingStatus: Record<NonNullable<BillingState>, { status: StatusName; label: string }> = {
  Trialing: { status: 'waiting', label: 'Free trial' },
  Active: { status: 'done', label: 'Active' },
  PastDue: { status: 'needsFixes', label: 'Payment failed' },
  Ended: { status: 'notInEffect', label: 'Ended' },
};

/** An amount in the plan's currency and the browser's locale: `$49`, `$49.50`. */
export function formatMoney(amount: number, currency: string): string {
  return new Intl.NumberFormat(locales, {
    style: 'currency',
    currency,
    minimumFractionDigits: Number.isInteger(amount) ? 0 : 2,
  }).format(amount);
}

/** A plan's price, or the words for a price its owner has not set. */
export const priceText = (plan: BillingPlan) => (plan.monthlyPrice === null ? 'Price not set' : `${formatMoney(plan.monthlyPrice, plan.currency)} a month`);

/** The price as the plans list shows it. With tax at Checkout it says so, so nobody expects the amount to be the whole charge. */
export const priceLine = (plan: BillingPlan, taxAtCheckout: boolean) =>
  plan.monthlyPrice !== null && taxAtCheckout ? `${priceText(plan)} plus tax where it applies` : priceText(plan);

const jobs = (count: number) => `${count} open ${count === 1 ? 'job' : 'jobs'}`;

/** What a plan lets someone open at once. */
export function planLimitText(plan: BillingPlan): string {
  if (plan.unlimited) return 'Unlimited open jobs';
  return plan.activeJobs === null ? 'Open job limit not set' : `Up to ${jobs(plan.activeJobs)}`;
}

/** The open jobs, under an Open jobs label: `3 of 5` against the plan's limit, or `3` with none (no plan after it ends, or an unlimited one). */
export function openJobsText(overview: Pick<BillingOverview, 'openJobs' | 'activeJobLimit'>): string {
  return overview.activeJobLimit === null ? `${overview.openJobs}` : `${overview.openJobs} of ${overview.activeJobLimit}`;
}

/** The margin as people say it: `20%`, or `17.5%` when it is not a whole number. */
export const marginText = (percent: number) => `${Number.isInteger(percent) ? percent : Number(percent.toFixed(2))}%`;

/** The calls this month, as a count under a label (the panel's first sentence already names ScreenHire's key). */
export const aiCallsText = (calls: number) => `${calls} AI ${calls === 1 ? 'call' : 'calls'}`;

/**
 * What else the AI usage panel says, one sentence each, depending on where the organization stands (docs/architecture/billing.md,
 * "AI usage"): how the trial treats its use, a provider that cannot bill it yet, and calls nothing could price.
 */
export function aiUsageNotes(usage: BillingAiUsage, state: BillingState | null): string[] {
  const notes: string[] = [];
  if (state === 'Trialing') {
    notes.push(
      usage.trialAllowance > 0
        ? `Your trial includes ${formatCost(usage.trialAllowance)} of AI use. Use above that is added to your first invoice.`
        : 'AI use during your trial is added to your first invoice.',
    );
  } else if (state === 'Ended') {
    notes.push('AI use is billed once you choose a plan.');
  } else if (!usage.metered) {
    notes.push('Billing for AI use is not set up here yet.');
  } else if (usage.trialAllowanceUsed > 0) {
    notes.push(`Your trial covered ${formatCost(usage.trialAllowanceUsed)} of AI use.`);
  }

  if (usage.callsWithUnknownCost > 0) {
    const n = usage.callsWithUnknownCost;
    notes.push(`${n} ${n === 1 ? 'call has' : 'calls have'} no known price, so ${n === 1 ? 'it is' : 'they are'} not charged.`);
  }

  return notes;
}

/** Whether someone without a subscription can start one here: on the trial, or after the subscription ended. */
export const mayChoosePlan = (state: BillingState | null) => state === 'Trialing' || state === 'Ended';
