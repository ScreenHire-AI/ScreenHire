// Every date the dashboard shows goes through this module (docs/design/design-system.md section 13). The API sends
// ISO instants only.

/** The browser's languages, then US English. Numbers are formatted in them too. */
export const locales = [...(globalThis.navigator?.languages ?? []), 'en-US'];

export const minute = 60_000;
export const hour = 60 * minute;
export const day = 24 * hour;

const date = new Intl.DateTimeFormat(locales, { dateStyle: 'medium' });
const dateTime = new Intl.DateTimeFormat(locales, { dateStyle: 'medium', timeStyle: 'short' });
const relative = new Intl.RelativeTimeFormat(locales, { numeric: 'auto' });

/** The date, for tables and old events: `Oct 20, 2026`. */
export const formatDate = (iso: string) => date.format(new Date(iso));

/** The exact date and time, for the `title` of a `<time>`: `Oct 20, 2026, 2:30 PM`. */
export const formatDateTime = (iso: string) => dateTime.format(new Date(iso));

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

/** A board's estimate, for a fact pill: `~20s`, `~1d`. */
export const eta = (iso: string, now: number) => `~${timeUntil(iso, now)}`;

const monthDay = new Intl.DateTimeFormat(locales, { month: 'short', day: 'numeric' });

/** The local day an instant falls on, as a key for grouping: `2026-10-25`. */
export function dayKey(iso: string): string {
  const at = new Date(iso);
  return `${at.getFullYear()}-${String(at.getMonth() + 1).padStart(2, '0')}-${String(at.getDate()).padStart(2, '0')}`;
}

/**
 * A day as a heading over that day's items, in the browser's locale: `Today`, `Yesterday`, `Oct 22`, or with the
 * year for another year, `Oct 22, 2025`.
 */
export function dayLabel(iso: string, now: number): string {
  const at = new Date(iso);
  const today = new Date(now);
  // A moment past this browser's clock, saved by a server whose clock runs ahead, is still today.
  const days = Math.max(
    0,
    Math.round((Date.UTC(today.getFullYear(), today.getMonth(), today.getDate()) - Date.UTC(at.getFullYear(), at.getMonth(), at.getDate())) / day),
  );
  if (days === 0 || days === 1) {
    const word = relative.format(-days, 'day');
    return word.charAt(0).toLocaleUpperCase(locales) + word.slice(1);
  }

  return at.getFullYear() === today.getFullYear() ? monthDay.format(at) : date.format(at);
}

/** Time ago in text: `now`, `5 minutes ago`, `yesterday`; the date once it is more than a week old. */
export function timeAgo(iso: string, now: number): string {
  const elapsed = Math.max(0, now - Date.parse(iso));
  if (elapsed >= 7 * day) return formatDate(iso);
  if (elapsed < minute) return relative.format(0, 'second');
  if (elapsed < hour) return relative.format(-Math.floor(elapsed / minute), 'minute');
  if (elapsed < day) return relative.format(-Math.floor(elapsed / hour), 'hour');
  return relative.format(-Math.floor(elapsed / day), 'day');
}
