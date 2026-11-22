import type {
  AiCreditPurchaseStatus,
  AiCreditSource,
  BillingAiUsage,
  BillingInvoiceStatus,
  BillingOverview,
  BillingPaymentMethod,
  BillingPlan,
  BillingState,
} from '@screenhire/api-client';
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

/**
 * An amount in its currency and the browser's locale, always with its two decimals, as people read money: `$49.00`, `$49.50`
 * (the owner's walk-through, 2026-11-21). A currency with no minor unit (yen) keeps none.
 */
export function formatMoney(amount: number, currency: string): string {
  return new Intl.NumberFormat(locales, { style: 'currency', currency }).format(amount);
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

/** The calls this month, as a count: `59 AI calls`. */
export const aiCallsText = (calls: number) => `${calls} AI ${calls === 1 ? 'call' : 'calls'}`;

/** The AI usage panel's one line for the month: the calls and the one amount for them, `59 AI calls, $0.05`. Never the provider's price or a margin. */
export const aiMonthText = (usage: Pick<BillingAiUsage, 'builtInCalls' | 'charge'>) => `${aiCallsText(usage.builtInCalls)}, ${formatCost(usage.charge)}`;

const cardBrands: Record<string, string> = {
  amex: 'American Express',
  diners: 'Diners Club',
  discover: 'Discover',
  eftpos_au: 'eftpos',
  jcb: 'JCB',
  mastercard: 'Mastercard',
  unionpay: 'UnionPay',
  visa: 'Visa',
};

/**
 * The payment method as people name it: `Visa ending in 4242` for a card, `Card ending in 4242` when the network is not one we
 * name, and for another kind of method its kind in words. Never the provider's name.
 */
export function paymentMethodText(method: BillingPaymentMethod): string {
  if (method.type === 'card') {
    const brand = method.brand ? (cardBrands[method.brand] ?? 'Card') : 'Card';
    return method.last4 ? `${brand} ending in ${method.last4}` : brand;
  }

  return method.type.endsWith('_debit') || method.type === 'us_bank_account' ? 'Bank account' : 'Payment method on file';
}

/** How an invoice's state reads as a status (section 3.2): paid and void are calm, due waits on a payment. */
export const invoiceStatus: Record<BillingInvoiceStatus, { status: StatusName; label: string }> = {
  Paid: { status: 'done', label: 'Paid' },
  Due: { status: 'waiting', label: 'Due' },
  Void: { status: 'notInEffect', label: 'Void' },
};

/**
 * What else the AI usage panel says, one sentence each (docs/architecture/billing.md, "AI credits"): checks that ran on the
 * organization's own key, and calls nothing could price. Neither draws on the credits.
 */
export function aiUsageNotes(usage: BillingAiUsage): string[] {
  const notes: string[] = [];
  if (usage.ownKeyCalls > 0) {
    const n = usage.ownKeyCalls;
    notes.push(`${n} ${n === 1 ? 'call' : 'calls'} ran on your own key, so ${n === 1 ? 'it used' : 'they used'} no credits.`);
  }

  if (usage.callsWithUnknownCost > 0) {
    const n = usage.callsWithUnknownCost;
    notes.push(`${n} ${n === 1 ? 'call has' : 'calls have'} no known price, so ${n === 1 ? 'it used' : 'they used'} no credits.`);
  }

  return notes;
}

/** AI credits in US dollars with two decimals: `$18.40`. A balance a little below zero (checks that ran together) shows as it is. */
export const formatCredits = (amount: number) => formatMoney(amount, 'USD');

/** How long credits last, from `expiryMonths` (null: never). */
export const creditExpiryText = (months: number | null) =>
  months === null ? 'Credits never expire.' : `Credits last ${months} ${months === 1 ? 'month' : 'months'} from when they are added.`;

/** Where a credit came from, in words. */
export const creditSourceLabels: Record<AiCreditSource, string> = {
  StartingCredit: 'Starting credit',
  Checkout: 'Bought',
  AutoReload: 'Auto-reload',
};

/** How a credit purchase went, as a status (section 3.2). */
export const creditPurchaseStatus: Record<AiCreditPurchaseStatus, { status: StatusName; label: string }> = {
  Paid: { status: 'done', label: 'Paid' },
  Included: { status: 'done', label: 'Included' },
  Failed: { status: 'needsFixes', label: 'Failed' },
};

/** Whether someone without a subscription can start one here: on the trial, or after the subscription ended. */
export const mayChoosePlan = (state: BillingState | null) => state === 'Trialing' || state === 'Ended';

/**
 * What the onboarding form says under its button about the free trial, from the days the API reports (`trialDays` on
 * `GET /v1/meta`, never written here). Nothing when there is no trial, which is how billing being off reads.
 */
export const trialLine = (days: number) =>
  days > 0 ? `Your free trial lasts ${days} ${days === 1 ? 'day' : 'days'} and needs no card.` : '';

/**
 * When the trial ends, for the sidebar's footer, or null: the line is only for a trial that is still running, so it is
 * null when billing is off, a plan was chosen, or the end has passed. The API sends `trialEndsAt` only while Trialing.
 */
export function trialEnd(overview: Pick<BillingOverview, 'available' | 'state' | 'trialEndsAt'> | undefined, now: number): string | null {
  const at = overview?.trialEndsAt;
  return overview?.available && overview.state === 'Trialing' && at && Date.parse(at) > now ? at : null;
}
