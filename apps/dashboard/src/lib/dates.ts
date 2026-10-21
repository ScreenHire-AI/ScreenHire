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

/** A board's estimate, for a fact pill: `~20s`, `~1d`. */
export const eta = (iso: string, now: number) => `~${timeUntil(iso, now)}`;

/** Time ago in text: `now`, `5 minutes ago`, `yesterday`; the date once it is more than a week old. */
export function timeAgo(iso: string, now: number): string {
  const elapsed = Math.max(0, now - Date.parse(iso));
  if (elapsed >= 7 * day) return formatDate(iso);
  if (elapsed < minute) return relative.format(0, 'second');
  if (elapsed < hour) return relative.format(-Math.floor(elapsed / minute), 'minute');
  if (elapsed < day) return relative.format(-Math.floor(elapsed / hour), 'hour');
  return relative.format(-Math.floor(elapsed / day), 'day');
}
