// Every date the dashboard shows goes through this module (docs/design/design-system.md section 13). The API sends
// ISO instants only. The person's language, time zone and date format from Your account (`#lib/date-settings.svelte.ts`)
// shape the formatters here; unset, they follow the browser.
import { dateSettings } from '#lib/date-settings.svelte.js';

/** The browser's languages, then US English. Numbers are formatted in them too. */
export const locales = [...(globalThis.navigator?.languages ?? []), 'en-US'];

export const minute = 60_000;
export const hour = 60 * minute;
export const day = 24 * hour;

interface Formatters {
  version: number;
  date: Intl.DateTimeFormat;
  dateTime: Intl.DateTimeFormat;
  time: Intl.DateTimeFormat;
  relative: Intl.RelativeTimeFormat;
  monthDay: Intl.DateTimeFormat;
  dayMonth: Intl.DateTimeFormat;
  /** Year, month and day in the person's zone, read as numbers to place an instant on a calendar day. */
  calendarDay: Intl.DateTimeFormat;
}

let built: Formatters | undefined;

/** The person's languages: their chosen one first, then the browser's. */
const personLocales = () => (dateSettings.language ? [dateSettings.language, ...locales] : locales);

/** The locale a fixed date format borrows its order from; the person's own for As your language writes it. */
function dateLocales(): string[] {
  switch (dateSettings.dateFormat) {
    case 'DayMonthYear':
      return ['en-GB'];
    case 'MonthDayYear':
      return ['en-US'];
    case 'Iso':
      // Swedish writes dates as year-month-day, which is ISO 8601.
      return ['sv-SE'];
    default:
      return personLocales();
  }
}

/** The person's time zone when the browser knows it; otherwise the browser's own. */
function personZone(): string | undefined {
  const timeZone = dateSettings.timeZone;
  if (!timeZone) return undefined;
  try {
    new Intl.DateTimeFormat('en-US', { timeZone });
    return timeZone;
  } catch {
    return undefined;
  }
}

// Reading `version` makes every caller in a template depend on it, so dates rewrite themselves after a change.
function formatters(): Formatters {
  const version = dateSettings.version;
  if (built?.version === version) return built;
  const timeZone = personZone();
  const iso = dateSettings.dateFormat === 'Iso';
  built = {
    version,
    date: new Intl.DateTimeFormat(dateLocales(), { dateStyle: iso ? 'short' : 'medium', timeZone }),
    dateTime: new Intl.DateTimeFormat(dateLocales(), { dateStyle: iso ? 'short' : 'medium', timeStyle: 'short', timeZone }),
    time: new Intl.DateTimeFormat(dateLocales(), { timeStyle: 'short', timeZone }),
    relative: new Intl.RelativeTimeFormat(personLocales(), { numeric: 'auto' }),
    monthDay: new Intl.DateTimeFormat(dateLocales(), iso ? { month: '2-digit', day: '2-digit', timeZone } : { month: 'short', day: 'numeric', timeZone }),
    dayMonth: new Intl.DateTimeFormat(dateLocales(), { day: 'numeric', month: 'long', timeZone }),
    calendarDay: new Intl.DateTimeFormat('en-US', { year: 'numeric', month: 'numeric', day: 'numeric', timeZone }),
  };
  return built;
}

/** The date, for tables and old events: `Oct 20, 2026`. */
export const formatDate = (iso: string) => formatters().date.format(new Date(iso));

/** The time alone, beside a `formatDate` where a narrow column may put it on a line of its own: `2:30 PM`. */
export const formatTime = (iso: string) => formatters().time.format(new Date(iso));

/** The exact date and time, for the `title` of a `<time>`: `Oct 20, 2026, 2:30 PM`. */
export const formatDateTime = (iso: string) => formatters().dateTime.format(new Date(iso));

/**
 * The date an instant falls on in another IANA zone, not the browser's: `Oct 20, 2026`. For a day someone chose in that
 * zone, such as an interview's window. A zone the browser does not know reads in the browser's own.
 */
export function formatDateIn(iso: string, zone: string): string {
  try {
    return new Intl.DateTimeFormat(dateLocales(), { dateStyle: dateSettings.dateFormat === 'Iso' ? 'short' : 'medium', timeZone: zone }).format(new Date(iso));
  } catch {
    return formatDate(iso);
  }
}

/**
 * Time until an instant, in its largest whole unit and the browser's locale: `20s`, `5m`, `4h`, `1d` narrow, or
 * `20 seconds`, `1 day` long.
 */
export function timeUntil(iso: string, now: number, unitDisplay: 'narrow' | 'long' = 'narrow'): string {
  const remaining = Date.parse(iso) - now;
  const [unit, amount] =
    remaining < minute
      ? (['second', Math.max(1, Math.ceil(remaining / 1000))] as const)
      : remaining < hour
        ? (['minute', Math.round(remaining / minute)] as const)
        : remaining < day
          ? (['hour', Math.round(remaining / hour)] as const)
          : (['day', Math.round(remaining / day)] as const);
  return new Intl.NumberFormat(locales, { style: 'unit', unit, unitDisplay }).format(amount);
}

/**
 * Time since an instant, in its largest whole unit, long, in the browser's locale: `5 minutes`, `2 hours`, `3 days`.
 * Never less than a minute, so a time that has only just passed still reads as a span.
 */
export function timeSince(iso: string, now: number): string {
  const elapsed = Math.max(0, now - Date.parse(iso));
  const [unit, amount] =
    elapsed < hour
      ? (['minute', Math.max(1, Math.floor(elapsed / minute))] as const)
      : elapsed < day
        ? (['hour', Math.floor(elapsed / hour)] as const)
        : (['day', Math.floor(elapsed / day)] as const);
  return new Intl.NumberFormat(locales, { style: 'unit', unit, unitDisplay: 'long' }).format(amount);
}

/** The day and month an instant falls on, in the person's zone and locale order: `20 October` in en-GB, `October 20` in en-US. */
export const formatDayMonth = (iso: string) => formatters().dayMonth.format(new Date(iso));

/** The calendar day an instant falls on in the person's zone, the one the dates beside it are written in. */
function calendarDay(at: Date): { year: number; month: number; day: number } {
  const parts = formatters().calendarDay.formatToParts(at);
  const part = (type: Intl.DateTimeFormatPartTypes) => Number(parts.find((p) => p.type === type)?.value);
  return { year: part('year'), month: part('month'), day: part('day') };
}

/** The person's day an instant falls on, as a key for grouping: `2026-10-25`. */
export function dayKey(iso: string): string {
  const { year, month, day: date } = calendarDay(new Date(iso));
  return `${year}-${String(month).padStart(2, '0')}-${String(date).padStart(2, '0')}`;
}

/**
 * A day as a heading over that day's items, in the person's zone and language: `Today`, `Yesterday`, `Oct 22`, or with
 * the year for another year, `Oct 22, 2025`.
 */
export function dayLabel(iso: string, now: number): string {
  const at = calendarDay(new Date(iso));
  const today = calendarDay(new Date(now));
  // A moment past this browser's clock, saved by a server whose clock runs ahead, is still today.
  const days = Math.max(0, Math.round((Date.UTC(today.year, today.month - 1, today.day) - Date.UTC(at.year, at.month - 1, at.day)) / day));
  if (days === 0 || days === 1) {
    const word = formatters().relative.format(-days, 'day');
    return word.charAt(0).toLocaleUpperCase(personLocales()) + word.slice(1);
  }

  return shortDate(iso, now);
}

/** A date for a tracker's caption: `Oct 31`, with the year only for another year (`Oct 31, 2025`). */
export function shortDate(iso: string, now: number): string {
  const at = new Date(iso);
  return calendarDay(at).year === calendarDay(new Date(now)).year ? formatters().monthDay.format(at) : formatters().date.format(at);
}

/** Time ago in text: `now`, `5 minutes ago`, `yesterday`; the date once it is more than a week old. */
export function timeAgo(iso: string, now: number): string {
  const elapsed = Math.max(0, now - Date.parse(iso));
  if (elapsed >= 7 * day) return formatDate(iso);
  const { relative } = formatters();
  if (elapsed < minute) return relative.format(0, 'second');
  if (elapsed < hour) return relative.format(-Math.floor(elapsed / minute), 'minute');
  if (elapsed < day) return relative.format(-Math.floor(elapsed / hour), 'hour');
  return relative.format(-Math.floor(elapsed / day), 'day');
}
