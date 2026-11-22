import type { AiKeySource, AiUsageLimitView, UsageUnit } from '@screenhire/api-client';
import { locales } from '#lib/dates.js';
import type { StatusName } from '#lib/status.js';

// The words and numbers of Billing's AI use section and the Activity log (docs/architecture/ai-usage.md). Costs are US dollars; a cost that nothing could
// price is "Unknown", never zero. Every cost is the one amount a customer sees for AI use: on ScreenHire's key what it is billed, on the
// organization's own key the provider's price, which that provider charges and ScreenHire never bills.

/** What an own key's amount is called wherever it shows: it is the provider's price, and only an estimate of the provider's bill. */
export const ownKeyCostLabel = 'Charged by your provider (estimate)';

const tokenFormat = new Intl.NumberFormat(locales);
const moneyFormat = new Intl.NumberFormat(locales, { style: 'currency', currency: 'USD', minimumFractionDigits: 2, maximumFractionDigits: 2 });

/**
 * Dollars in two decimals, as people read money ($0.05, never $0.0547: the owner's walk-through, 2026-11-21). An amount
 * above zero that would round to $0.00 says "Under $0.01", so a call that cost something never reads as free. The
 * Activity log's CSV keeps every digit.
 */
const money = (amount: number): string => (amount > 0 && amount < 0.005 ? `Under ${moneyFormat.format(0.01)}` : moneyFormat.format(amount));

/** A count, or an empty string for one the provider did not report (a blank cell, with the word for screen readers where it is drawn). */
export const formatTokens = (count: number | null | undefined): string => (count === null || count === undefined ? '' : tokenFormat.format(count));

const dayFormat = new Intl.DateTimeFormat(locales, { dateStyle: 'medium' });

/** A calendar day (`2026-11-08`) in the person's own format. */
export function formatDay(date: string): string {
  const [year, month, day] = date.split('-').map(Number);
  return dayFormat.format(new Date(year, month - 1, day));
}

export const formatCost = (cost: number | null | undefined): string => (cost === null || cost === undefined ? 'Unknown' : money(cost));

/** An amount in the unit it is counted in: "1,000,000 tokens" or "$50.00". */
export const formatAmount = (amount: number, unit: UsageUnit): string => (unit === 'Money' ? money(amount) : `${tokenFormat.format(amount)} tokens`);

export const keySourceLabels: Record<AiKeySource, string> = { BuiltIn: "ScreenHire's key", OwnKey: 'Your own key' };

/** The words of one outcome, and whether it is a failure. */
export const outcomeLabels = { Ok: 'OK', Error: 'Error', TimedOut: 'Timed out' } as const;

/** The check as a person reads it. An unknown check shows as it is named. */
export const checkLabels: Record<string, string> = {
  'consistency.extraction': 'Consistency, reading a source',
  'consistency.comparison': 'Consistency, comparing sources',
  'model-test': 'Connection test',
};

/** Where a budget stands: within it, close to it, or past it. Status is an icon, a colour and a label (design system 3.2). */
export function budgetStatus(limit: AiUsageLimitView): { status: StatusName; label: string } {
  if (limit.percent >= 100) return { status: 'needsFixes', label: 'Reached' };
  if (limit.percent >= 80) return { status: 'review', label: 'Close' };
  return { status: 'done', label: 'Within' };
}

/** Every call's id is a version 7 uuid, newest first; the first page has no cursor. */
export const pageSize = 50;
