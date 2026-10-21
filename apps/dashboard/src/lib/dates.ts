/**
 * Every date the dashboard shows goes through this module (docs/design/design-system.md section 13): time ago in
 * text, the date in tables and for anything older than a week, the exact time on hover. The browser's languages come
 * first and US English is the fallback. The API sends ISO instants only.
 */

/** The browser's languages, then US English. Numbers are formatted in them too. */
export const locales = [...(globalThis.navigator?.languages ?? []), 'en-US'];

const minute = 60_000;
const hour = 60 * minute;
const day = 24 * hour;

const date = new Intl.DateTimeFormat(locales, { dateStyle: 'medium' });
const dateTime = new Intl.DateTimeFormat(locales, { dateStyle: 'medium', timeStyle: 'short' });
const relative = new Intl.RelativeTimeFormat(locales, { numeric: 'auto' });

/** The date, for tables and old events: `Oct 20, 2026`. */
export const formatDate = (iso: string) => date.format(new Date(iso));

/** The exact date and time, for the `title` of a `<time>`: `Oct 20, 2026, 2:30 PM`. */
export const formatDateTime = (iso: string) => dateTime.format(new Date(iso));

/** Time ago in text: `now`, `5 minutes ago`, `yesterday`; the date once it is more than a week old. */
export function timeAgo(iso: string, now: number): string {
  const elapsed = Math.max(0, now - Date.parse(iso));
  if (elapsed >= 7 * day) return formatDate(iso);
  if (elapsed < minute) return relative.format(0, 'second');
  if (elapsed < hour) return relative.format(-Math.floor(elapsed / minute), 'minute');
  if (elapsed < day) return relative.format(-Math.floor(elapsed / hour), 'hour');
  return relative.format(-Math.floor(elapsed / day), 'day');
}
