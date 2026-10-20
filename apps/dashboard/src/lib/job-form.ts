import type { JobLocation, SalaryPeriod, WorkplaceType } from '@screenhire/api-client';
import { areCountryCodes, countryCodes, paragraphs } from './forms.js';

// The posting fields the New job dialog and the job details sheet share, checked as JobPublishValidator and
// JobContentValidator will check them, so a job made or edited in the dashboard can be published.

/** Where the work happens, as the forms hold it: countries for a remote job, one city and country otherwise. */
export interface WhereFields {
  workplaceType: '' | WorkplaceType;
  countries: string;
  city: string;
  country: string;
}

type Issue<T> = (path: keyof T & string, message: string) => void;

/** The form's workplace values: empty until one is chosen. */
export const workplaceValues = ['', 'Remote', 'Hybrid', 'Onsite'] as const satisfies readonly WhereFields['workplaceType'][];

export const workplaceOptions: { value: WorkplaceType; label: string }[] = [
  { value: 'Remote', label: 'Remote' },
  { value: 'Hybrid', label: 'Hybrid' },
  { value: 'Onsite', label: 'On-site' },
];

export function checkWhere(value: WhereFields, issue: Issue<WhereFields>): void {
  if (value.workplaceType === '') {
    issue('workplaceType', 'Choose where the work happens.');
  } else if (value.workplaceType === 'Remote') {
    const codes = countryCodes(value.countries);
    if (codes.length === 0) issue('countries', 'List the countries candidates can work from, such as GB, IE.');
    else if (!areCountryCodes(codes)) issue('countries', 'Use two-letter country codes, such as GB.');
  } else if (!areCountryCodes([value.country.trim().toUpperCase()])) {
    issue('country', 'Give the country as a two-letter code, such as GB.');
  }
}

/**
 * The request's location fields. A remote job lists countries and no location. Any other job has the one location the
 * form edits in place of `current`'s first, which keeps its region while the city and country stay the same, followed
 * by the rest of `current` as they were.
 */
export function whereBody(value: WhereFields, current: readonly JobLocation[] = []) {
  const remote = value.workplaceType === 'Remote';
  const [first, ...others] = current;
  const city = value.city.trim() || null;
  const country = value.country.trim().toUpperCase();
  const region = first && first.city === city && first.country === country ? first.region : null;
  return {
    workplaceType: value.workplaceType || null,
    applicantLocationRequirements: remote ? countryCodes(value.countries) : null,
    locations: remote ? null : [{ city, region, country }, ...others],
  };
}

/** The form's view of a job's location fields. */
export function whereFields(job: { workplaceType: WorkplaceType; applicantLocationRequirements: readonly string[]; locations: readonly JobLocation[] }): WhereFields {
  const first = job.locations[0];
  return {
    workplaceType: job.workplaceType,
    countries: job.applicantLocationRequirements.join(', '),
    city: first?.city ?? '',
    country: first?.country ?? '',
  };
}

/** A salary range as the form holds it: figures as typed, an ISO 4217 currency and a period. */
export interface SalaryFields {
  salaryMin: string;
  salaryMax: string;
  currency: string;
  period: SalaryPeriod;
}

export const periodValues = ['Year', 'Month', 'Week', 'Day', 'Hour'] as const satisfies readonly SalaryPeriod[];

export const periodOptions: { value: SalaryPeriod; label: string }[] = [
  { value: 'Year', label: 'per year' },
  { value: 'Month', label: 'per month' },
  { value: 'Week', label: 'per week' },
  { value: 'Day', label: 'per day' },
  { value: 'Hour', label: 'per hour' },
];

const amount = (text: string): number | null => (text.trim() === '' ? null : Number(text.trim()));

export function checkSalary(value: SalaryFields, issue: Issue<SalaryFields>): void {
  const [min, max] = [amount(value.salaryMin), amount(value.salaryMax)];
  for (const [path, figure] of [['salaryMin', min], ['salaryMax', max]] as const) {
    if (figure !== null && (!Number.isFinite(figure) || figure < 0)) issue(path, 'Enter an amount such as 55000, or leave it empty.');
  }

  if (min !== null && max !== null && Number.isFinite(min) && Number.isFinite(max) && min > max) {
    issue('salaryMax', 'The top of the range is below the bottom.');
  }

  if ((min !== null || max !== null) && !/^[A-Za-z]{3}$/.test(value.currency.trim())) {
    issue('currency', 'Use a three-letter currency code, such as GBP.');
  }
}

/** The request's salary: null when neither figure is given. */
export function salaryBody(value: SalaryFields) {
  const [min, max] = [amount(value.salaryMin), amount(value.salaryMax)];
  return min === null && max === null ? null : { min, max, currency: value.currency.trim().toUpperCase(), period: value.period };
}

/** The date input's value for a closing instant. */
export const closingDate = (instant: string | null | undefined): string => (instant ? instant.slice(0, 10) : '');

/**
 * A closing date (`YYYY-MM-DD`, the date input's value) as the last second of that day in UTC, or null. The date of
 * `original` keeps `original` as it is, so a closing time set through the API survives an edit of something else.
 */
export const closingInstant = (date: string, original?: string | null): string | null =>
  original && date === closingDate(original) ? original : date ? `${date}T23:59:59Z` : null;

/**
 * A job description's HTML as plain paragraphs for a text box: a block per paragraph, heading or list item (list items
 * start with "- "), separated by blank lines. {@link paragraphs} turns the text back into HTML: paragraphs and lists come
 * back as they were, headings as paragraphs.
 */
export function descriptionText(html: string): string {
  const document = new DOMParser().parseFromString(html, 'text/html');
  const blocks: string[] = [];
  for (const element of document.body.querySelectorAll('p, h1, h2, h3, h4, h5, h6, li')) {
    if (element.querySelector('p, li')) continue; // a wrapper; its children are blocks of their own
    const line = (element.textContent ?? '').replace(/\s+/g, ' ').trim();
    if (line) blocks.push(element.tagName === 'LI' ? `- ${line}` : line);
  }

  return blocks.length > 0 ? blocks.join('\n\n') : (document.body.textContent ?? '').trim();
}

/** The description to send: the job's own HTML when the text was not changed, so its formatting survives. */
export const descriptionBody = (text: string, original: { html: string; text: string }): string | null =>
  text === original.text ? original.html : text.trim() ? paragraphs(text) : null;
