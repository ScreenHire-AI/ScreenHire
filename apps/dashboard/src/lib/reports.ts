import { locales } from '#lib/dates.js';
import { parseDay } from '#lib/report-range.js';

// Words and numbers for the Reports page (docs/architecture/reports.md). Counts and shares use the browser's locale.
// A funnel row is named by its stage kind, as `stageKindLabels` in #lib/workflow.ts writes it.

const counts = new Intl.NumberFormat(locales);
const percent = new Intl.NumberFormat(locales, { style: 'percent', maximumFractionDigits: 0 });
const wholeDays = new Intl.NumberFormat(locales, { style: 'unit', unit: 'day', unitDisplay: 'long', maximumFractionDigits: 0 });

/** A count: `1,284`. */
export const countText = (value: number) => counts.format(value);

/** The share of `of` that `part` is, as `62%`, or null when there is nothing to share out. */
export function shareText(part: number, of: number): string | null {
  return of > 0 ? percent.format(part / of) : null;
}

/** A median in whole days: `21 days`, `1 day`. */
export const daysText = (value: number) => wholeDays.format(Math.round(value));

/** A count and its noun: `1 hire`, `3 hires`. */
export const counted = (value: number, one: string, many: string) => `${countText(value)} ${value === 1 ? one : many}`;

/** How a number moved against the previous period, in words, with the way the arrow points. */
export interface Change {
  text: string;
  direction: 'up' | 'down' | 'none';
}

/**
 * A headline number's change in words ("18 more than before"), or null when the previous period has no number to compare.
 * `kind: 'days'` is a median in days, where lower reads as faster ("3 days faster"); medians compare as the whole days
 * they show. The arrow follows the number, never whether the change is good: the words and arrow stay neutral.
 */
export function changeText(current: number | null, previous: number | null | undefined, kind: 'count' | 'days' = 'count'): Change | null {
  if (current === null || previous === null || previous === undefined) return null;
  const now = kind === 'days' ? Math.round(current) : current;
  const before = kind === 'days' ? Math.round(previous) : previous;
  const difference = now - before;
  if (difference === 0) return { text: 'No change', direction: 'none' };
  const amount = Math.abs(difference);
  const direction = difference > 0 ? 'up' : 'down';
  if (kind === 'days') return { text: `${daysText(amount)} ${difference > 0 ? 'slower' : 'faster'}`, direction };
  return { text: `${countText(amount)} ${difference > 0 ? 'more' : 'fewer'} than before`, direction };
}

/** The share of applications that were hired: `18%`; null with no applications. */
export const hireRate = (hires: number, applications: number) => shareText(hires, applications);

/** A funnel row, as far as the biggest drop needs it. */
export interface DropRow {
  kind: string;
  applications: number;
  movedOn: number | null;
}

/** A row with fewer applications than this has too few for its share to mean anything. */
export const minDropApplications = 3;

/**
 * The stage kind where the smallest share moved on to a later stage, among rows with enough applications (so 1 of 1
 * never wins), the earlier row on a tie. Null when every share is 100% or no row has enough applications.
 */
export function biggestDrop(rows: readonly DropRow[]): string | null {
  let worst: { kind: string; share: number } | null = null;
  for (const row of rows) {
    if (row.movedOn === null || row.applications < minDropApplications) continue;
    const share = row.movedOn / row.applications;
    if (share < 1 && (worst === null || share < worst.share)) worst = { kind: row.kind, share };
  }
  return worst?.kind ?? null;
}

const weekName = new Intl.DateTimeFormat(locales, { day: 'numeric', month: 'short' });

/** A week by its Monday, as `3 Nov`; `weekStart` is the API's `YYYY-MM-DD`. */
export function weekText(weekStart: string): string {
  const day = parseDay(weekStart);
  return day ? weekName.format(day) : weekStart;
}
