import type { ApplicationSource, EmploymentType, JobSalary } from '@screenhire/api-client';

const minute = 60_000;
const hour = 60 * minute;
const day = 24 * hour;

/** Compact age for tables and strips: `now`, `30m`, `3h`, `2d`, `5w`. */
export function age(iso: string, now: number): string {
  const elapsed = Math.max(0, now - Date.parse(iso));
  if (elapsed < minute) return 'now';
  if (elapsed < hour) return `${Math.floor(elapsed / minute)}m`;
  if (elapsed < day) return `${Math.floor(elapsed / hour)}h`;
  if (elapsed < 7 * day) return `${Math.floor(elapsed / day)}d`;
  return `${Math.floor(elapsed / (7 * day))}w`;
}

/** Compact time until an instant: `~20s`, `~5m`, `~4h`, `~1d`. */
export function eta(iso: string, now: number): string {
  const remaining = Date.parse(iso) - now;
  if (remaining < minute) return `~${Math.max(1, Math.ceil(remaining / 1000))}s`;
  if (remaining < hour) return `~${Math.round(remaining / minute)}m`;
  if (remaining < day) return `~${Math.round(remaining / hour)}h`;
  return `~${Math.round(remaining / day)}d`;
}

const dateTime = new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' });
const shortDate = new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short' });

export const formatDateTime = (iso: string) => dateTime.format(new Date(iso));
export const formatShortDate = (iso: string) => shortDate.format(new Date(iso));

const employmentLabels: Record<EmploymentType, string> = {
  FullTime: 'Full-time',
  PartTime: 'Part-time',
  Contract: 'Contract',
  Temporary: 'Temporary',
  Internship: 'Internship',
};

export const employmentLabel = (type: EmploymentType) => employmentLabels[type];

const periodSuffix: Record<string, string> = { Hour: '/hr', Day: '/day', Week: '/wk', Month: '/mo', Year: '' };

/** A salary range as the job header shows it: `£70K–£85K`, `from £30K`, `up to £14/hr`. */
export function formatSalary(salary: JobSalary): string {
  const money = new Intl.NumberFormat('en-GB', {
    style: 'currency',
    currency: salary.currency,
    notation: 'compact',
    maximumFractionDigits: 1,
  });
  const suffix = periodSuffix[salary.period ?? 'Year'] ?? '';
  const { min, max } = salary;
  if (min !== null && max !== null) return `${money.format(min)}–${money.format(max)}${suffix}`;
  if (min !== null) return `from ${money.format(min)}${suffix}`;
  if (max !== null) return `up to ${money.format(max)}${suffix}`;
  return '';
}

const sourceLabels: Record<ApplicationSource, string> = { CareersSite: 'careers', Recruiter: 'recruiter' };

/** Where an application came from, as the mono source column shows it. */
export const sourceLabel = (source: ApplicationSource) => sourceLabels[source];

/** Up to two initials for an avatar: "Casey Morgan" is "CM". */
export function initials(name: string): string {
  return name
    .split(/[\s@.]+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]!.toUpperCase())
    .join('');
}
