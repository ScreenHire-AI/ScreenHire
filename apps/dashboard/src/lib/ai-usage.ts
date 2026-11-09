import type { AiKeySource, AiUsageLimitView, UsageUnit } from '@screenhire/api-client';
import { locales } from '#lib/dates.js';
import type { StatusName } from '#lib/status.js';

// The words and numbers of the Usage tab and the Activity log (docs/architecture/ai-usage.md). Costs are US dollars; a cost that nothing could
// price is "Unknown", never zero.

const tokenFormat = new Intl.NumberFormat(locales);
const moneyFormat = new Intl.NumberFormat(locales, { style: 'currency', currency: 'USD', minimumFractionDigits: 2, maximumFractionDigits: 4 });

/** A count, or an empty string for one the provider did not report (a blank cell, with the word for screen readers where it is drawn). */
export const formatTokens = (count: number | null | undefined): string => (count === null || count === undefined ? '' : tokenFormat.format(count));

const dayFormat = new Intl.DateTimeFormat(locales, { dateStyle: 'medium' });

/** A calendar day (`2026-11-08`) in the person's own format. */
export function formatDay(date: string): string {
  const [year, month, day] = date.split('-').map(Number);
  return dayFormat.format(new Date(year, month - 1, day));
}

export const formatCost = (cost: number | null | undefined): string => (cost === null || cost === undefined ? 'Unknown' : moneyFormat.format(cost));

/** An amount in the unit it is counted in: "1,000,000 tokens" or "$50.00". */
export const formatAmount = (amount: number, unit: UsageUnit): string => (unit === 'Money' ? moneyFormat.format(amount) : `${tokenFormat.format(amount)} tokens`);

export const unitLabels: Record<UsageUnit, string> = { Tokens: 'Tokens', Money: 'Dollars' };

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
