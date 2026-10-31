import { locales } from '#lib/dates.js';

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
