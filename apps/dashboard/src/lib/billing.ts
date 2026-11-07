import type { BillingOverview, BillingPlan, BillingState } from '@screenhire/api-client';
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

const jobs = (count: number) => `${count} open ${count === 1 ? 'job' : 'jobs'}`;

/** What a plan lets someone open at once. */
export function planLimitText(plan: BillingPlan): string {
  if (plan.unlimited) return 'Unlimited open jobs';
  return plan.activeJobs === null ? 'Open job limit not set' : `Up to ${jobs(plan.activeJobs)}`;
}

/** The open jobs against the plan's limit: `3 of 5 open jobs`, or `3 open jobs, no limit`. */
export function openJobsText(overview: Pick<BillingOverview, 'openJobs' | 'activeJobLimit'>): string {
  return overview.activeJobLimit === null ? `${jobs(overview.openJobs)}, no limit` : `${overview.openJobs} of ${jobs(overview.activeJobLimit)}`;
}

/** Whether someone without a subscription can start one here: on the trial, or after the subscription ended. */
export const mayChoosePlan = (state: BillingState | null) => state === 'Trialing' || state === 'Ended';
