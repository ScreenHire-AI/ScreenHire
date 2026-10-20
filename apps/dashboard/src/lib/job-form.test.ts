import { describe, expect, it } from 'vitest';
import {
  checkSalary,
  checkWhere,
  closingDate,
  closingInstant,
  descriptionBody,
  descriptionText,
  salaryBody,
  whereBody,
  whereFields,
  type SalaryFields,
  type WhereFields,
} from './job-form.js';

const problems = <T>(check: (value: T, issue: (path: string, message: string) => void) => void, value: T) => {
  const found: Record<string, string> = {};
  check(value, (path, message) => (found[path] = message));
  return found;
};

const where = (overrides: Partial<WhereFields>): WhereFields => ({ workplaceType: 'Remote', countries: '', city: '', country: '', ...overrides });
const salary = (overrides: Partial<SalaryFields>): SalaryFields => ({ salaryMin: '', salaryMax: '', currency: 'GBP', period: 'Year', ...overrides });

describe('where the work happens', () => {
  it('needs a workplace, countries for a remote job and a country code otherwise', () => {
    expect(problems(checkWhere, where({ workplaceType: '' }))).toEqual({ workplaceType: 'Choose where the work happens.' });
    expect(problems(checkWhere, where({}))).toEqual({ countries: 'List the countries candidates can work from, such as GB, IE.' });
    expect(problems(checkWhere, where({ countries: 'England' }))).toEqual({ countries: 'Use two-letter country codes, such as GB.' });
    expect(problems(checkWhere, where({ workplaceType: 'Onsite', country: 'England' }))).toEqual({
      country: 'Give the country as a two-letter code, such as GB.',
    });
    expect(problems(checkWhere, where({ workplaceType: 'Hybrid', city: 'Leeds', country: 'gb' }))).toEqual({});
  });

  it('sends countries for a remote job and the edited location first for any other', () => {
    expect(whereBody(where({ countries: 'gb ie' }))).toEqual({ workplaceType: 'Remote', applicantLocationRequirements: ['GB', 'IE'], locations: null });
    const leeds = { city: 'Leeds', region: 'West Yorkshire', country: 'GB' };
    const york = { city: 'York', region: null, country: 'GB' };
    expect(whereBody(where({ workplaceType: 'Onsite', city: ' Leeds ', country: 'gb' }), [leeds, york])).toEqual({
      workplaceType: 'Onsite',
      applicantLocationRequirements: null,
      locations: [leeds, york],
    });
    // Another city is another place: the old region does not follow it.
    expect(whereBody(where({ workplaceType: 'Onsite', city: 'Hull', country: 'GB' }), [leeds, york]).locations).toEqual([
      { city: 'Hull', region: null, country: 'GB' },
      york,
    ]);
  });

  it("reads a job's fields back into the form", () => {
    expect(whereFields({ workplaceType: 'Remote', applicantLocationRequirements: ['GB', 'IE'], locations: [] })).toEqual({
      workplaceType: 'Remote',
      countries: 'GB, IE',
      city: '',
      country: '',
    });
    expect(whereFields({ workplaceType: 'Onsite', applicantLocationRequirements: [], locations: [{ city: 'Manchester', region: 'England', country: 'GB' }] })).toMatchObject({
      city: 'Manchester',
      country: 'GB',
    });
  });
});

describe('salary', () => {
  it('is optional, but a range needs amounts in order and a currency code', () => {
    expect(problems(checkSalary, salary({}))).toEqual({});
    expect(problems(checkSalary, salary({ salaryMin: 'lots' }))).toEqual({ salaryMin: 'Enter an amount such as 55000, or leave it empty.' });
    expect(problems(checkSalary, salary({ salaryMin: '90000', salaryMax: '70000' }))).toEqual({ salaryMax: 'The top of the range is below the bottom.' });
    expect(problems(checkSalary, salary({ salaryMax: '60000', currency: 'pounds' }))).toEqual({ currency: 'Use a three-letter currency code, such as GBP.' });
  });

  it('sends null without figures, and the figures as numbers otherwise', () => {
    expect(salaryBody(salary({}))).toBeNull();
    expect(salaryBody(salary({ salaryMin: '70000', salaryMax: ' 85000 ', currency: 'eur', period: 'Month' }))).toEqual({
      min: 70000,
      max: 85000,
      currency: 'EUR',
      period: 'Month',
    });
  });
});

describe('closing date', () => {
  it('is the end of the chosen day in UTC, and reads back as the date', () => {
    expect(closingInstant('2026-11-30')).toBe('2026-11-30T23:59:59Z');
    expect(closingInstant('')).toBeNull();
    expect(closingDate('2026-11-30T23:59:59+00:00')).toBe('2026-11-30');
    expect(closingDate(null)).toBe('');
  });

  it("keeps the job's own instant while its date is unchanged", () => {
    expect(closingInstant('2026-11-30', '2026-11-30T12:00:00+00:00')).toBe('2026-11-30T12:00:00+00:00');
    expect(closingInstant('2026-12-01', '2026-11-30T12:00:00+00:00')).toBe('2026-12-01T23:59:59Z');
    expect(closingInstant('', '2026-11-30T12:00:00+00:00')).toBeNull();
  });
});

describe('description', () => {
  const html = '<p>Join our payments team.</p><h2>What you will do</h2><ul><li>Ship features.</li><li>Review code.</li></ul>';

  it('reads as a block per paragraph, heading and list item', () => {
    expect(descriptionText(html)).toBe('Join our payments team.\n\nWhat you will do\n\n- Ship features.\n\n- Review code.');
    expect(descriptionText('Just text')).toBe('Just text');
  });

  it('keeps the original HTML when the text is untouched, and makes paragraphs when it changed', () => {
    const original = { html, text: descriptionText(html) };
    expect(descriptionBody(original.text, original)).toBe(html);
    expect(descriptionBody('New intro.\n\nMore.', original)).toBe('<p>New intro.</p><p>More.</p>');
    expect(descriptionBody('  ', original)).toBeNull();
  });
});
