import { locales } from '#lib/dates.js';

// The Reports page's date range (docs/architecture/reports.md). People pick days in their own time zone (design system
// W8), so the browser turns them into instants at local midnights, and the API never guesses a zone: `from` is the
// first day's midnight and `to` the midnight after the last day.

/** The ranges offered first: the last 30, 90 or 365 days, today included. */
export const presets = [30, 90, 365] as const;

export type Preset = (typeof presets)[number];

/** A preset, or a custom range of whole local days, both ends included, as `YYYY-MM-DD`. */
export type ReportRange = { kind: 'preset'; days: Preset } | { kind: 'custom'; start: string; end: string };

const defaultDays: Preset = 30;

export const defaultRange: ReportRange = { kind: 'preset', days: defaultDays };

/**
 * The longest custom range: 5 years of days. One day under the API's limit of 1827 days of elapsed time, because a
 * clock change inside the range can add an hour to the local days it covers.
 */
export const maxDays = 1826;

const dateText = /^(\d{4})-(\d{2})-(\d{2})$/;

/** The local midnight starting `text` (`YYYY-MM-DD`), or null when it is not a real date. */
export function parseDay(text: string | null | undefined): Date | null {
  const match = text ? dateText.exec(text) : null;
  if (!match) return null;
  const [year, month, day] = [Number(match[1]), Number(match[2]), Number(match[3])];
  const at = new Date(year, month - 1, day);
  return at.getFullYear() === year && at.getMonth() === month - 1 && at.getDate() === day ? at : null;
}

/** A local day as `YYYY-MM-DD`, the value of a date field. */
export function dayText(at: Date): string {
  return `${at.getFullYear()}-${String(at.getMonth() + 1).padStart(2, '0')}-${String(at.getDate()).padStart(2, '0')}`;
}

/** The local midnight `days` days after the one starting `at`'s day (negative for before), across clock changes. */
function midnight(at: Date, days = 0): Date {
  return new Date(at.getFullYear(), at.getMonth(), at.getDate() + days);
}

/** How many days a custom range covers, both ends included. */
export function daysCovered(start: Date, end: Date): number {
  return Math.round((midnight(end, 1).getTime() - start.getTime()) / 86_400_000);
}

/** What is wrong with a custom range, in words, or null when it is fine. */
export function customRangeProblem(start: string, end: string): string | null {
  const first = parseDay(start);
  const last = parseDay(end);
  if (!first || !last) return 'Pick both dates.';
  if (first > last) return 'The range has to end on or after its first day.';
  if (daysCovered(first, last) > maxDays) return 'A report covers 5 years at most.';
  return null;
}

/** The range in the address (`?range=90`, or `?from=2026-01-01&to=2026-03-31`); the default when it says none or nonsense. */
export function readRange(params: Pick<URLSearchParams, 'get'>): ReportRange {
  const start = params.get('from');
  const end = params.get('to');
  if (start !== null && end !== null && customRangeProblem(start, end) === null) return { kind: 'custom', start, end };
  const days = Number(params.get('range'));
  return presets.find((preset) => preset === days) ? { kind: 'preset', days: days as Preset } : defaultRange;
}

/** Writes `range` into `url`'s search, leaving the default out so a plain address means the last 30 days. */
export function writeRange(url: URL, range: ReportRange): void {
  url.searchParams.delete('range');
  url.searchParams.delete('from');
  url.searchParams.delete('to');
  if (range.kind === 'custom') {
    url.searchParams.set('from', range.start);
    url.searchParams.set('to', range.end);
  } else if (range.days !== defaultDays) {
    url.searchParams.set('range', String(range.days));
  }
}

/** The instants the API takes: the first day's local midnight, and the local midnight after the last day. */
export function rangeInstants(range: ReportRange, now: Date): { from: string; to: string } {
  if (range.kind === 'custom') {
    const start = parseDay(range.start) ?? midnight(now);
    const end = parseDay(range.end) ?? midnight(now);
    return { from: start.toISOString(), to: midnight(end, 1).toISOString() };
  }
  return { from: midnight(now, 1 - range.days).toISOString(), to: midnight(now, 1).toISOString() };
}

const days = new Intl.DateTimeFormat(locales, { dateStyle: 'medium' });

/** The range in words, for its button: "Last 30 days", or the two days as the browser's locale writes a range. */
export function rangeLabel(range: ReportRange): string {
  if (range.kind === 'preset') return `Last ${range.days} days`;
  const start = parseDay(range.start);
  const end = parseDay(range.end);
  return start && end ? days.formatRange(start, end) : 'Custom range';
}

/** Two ranges are the same choice. */
export function sameRange(left: ReportRange, right: ReportRange): boolean {
  return left.kind === 'preset' && right.kind === 'preset'
    ? left.days === right.days
    : left.kind === 'custom' && right.kind === 'custom' && left.start === right.start && left.end === right.end;
}
