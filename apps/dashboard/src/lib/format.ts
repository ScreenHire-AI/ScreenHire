import type { ApplicationSource, EmploymentType, JobSalary } from '@screenhire/api-client';
import { day, hour, locales, minute } from '#lib/dates.js';

/** Compact time until an instant: `~20s`, `~5m`, `~4h`, `~1d`. */
export function eta(iso: string, now: number): string {
  const remaining = Date.parse(iso) - now;
  if (remaining < minute) return `~${Math.max(1, Math.ceil(remaining / 1000))}s`;
  if (remaining < hour) return `~${Math.round(remaining / minute)}m`;
  if (remaining < day) return `~${Math.round(remaining / hour)}h`;
  return `~${Math.round(remaining / day)}d`;
}

const employmentLabels: Record<EmploymentType, string> = {
  FullTime: 'Full-time',
  PartTime: 'Part-time',
  Contract: 'Contract',
  Temporary: 'Temporary',
  Internship: 'Internship',
};

export const employmentLabel = (type: EmploymentType) => employmentLabels[type];

const periodSuffix: Record<string, string> = { Hour: '/hr', Day: '/day', Week: '/wk', Month: '/mo', Year: '' };

/** A salary range as the job header shows it, in the browser's locale: `£70K-£85K`, `from £30K`, `up to £14/hr`. */
export function formatSalary(salary: JobSalary): string {
  const money = new Intl.NumberFormat(locales, {
    style: 'currency',
    currency: salary.currency,
    notation: 'compact',
    maximumFractionDigits: 1,
  });
  const suffix = periodSuffix[salary.period ?? 'Year'] ?? '';
  const { min, max } = salary;
  if (min !== null && max !== null) return `${money.format(min)}-${money.format(max)}${suffix}`;
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
