import type { ApplicationSource, EmploymentType, JobSalary } from '@screenhire/api-client';
import { locales } from '#lib/dates.js';

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

const sourceLabels: Record<ApplicationSource, string> = { CareersSite: 'Careers page', Recruiter: 'Recruiter' };

/**
 * The site an application came from when the careers page was embedded in the organization's own site
 * (`https://www.example.com` reads `www.example.com`), as people know it.
 */
export const siteName = (site: string) => site.replace(/^https?:\/\//, '');

/**
 * Where an application came from, as a table's Source column shows it: the organization's own site when the careers page
 * was embedded in it (docs/architecture/careers-widget.md).
 */
export const sourceLabel = (source: ApplicationSource, sourceSite?: string | null) => (sourceSite ? siteName(sourceSite) : sourceLabels[source]);

const sourceNames: Record<ApplicationSource, string> = { CareersSite: 'Careers page', Recruiter: 'Added by a recruiter' };

/** Where an application came from, as a detail page's Source fact. */
export const sourceName = (source: ApplicationSource) => sourceNames[source];

/** Up to two initials for an avatar: "Casey Morgan" is "CM". */
export function initials(name: string): string {
  return name
    .split(/[\s@.]+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]!.toUpperCase())
    .join('');
}

const kilobytes = new Intl.NumberFormat(locales, { style: 'unit', unit: 'kilobyte', maximumFractionDigits: 0 });
const megabytes = new Intl.NumberFormat(locales, { style: 'unit', unit: 'megabyte', maximumFractionDigits: 1 });

/** A file's size for a fact pill, in the browser's locale: `12 kB`, `2.4 MB`. */
export function fileSize(bytes: number): string {
  if (bytes < 1_000_000) return kilobytes.format(Math.max(1, Math.round(bytes / 1000)));
  return megabytes.format(bytes / 1_000_000);
}
