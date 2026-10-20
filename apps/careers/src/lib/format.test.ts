import { describe, expect, it } from 'vitest';
import { formatSalary, label } from './format';

describe('label', () => {
  it.each([
    ['Onsite', 'On-site'],
    ['Hybrid', 'Hybrid'],
    ['Remote', 'Remote'],
    ['FullTime', 'Full-time'],
    ['PartTime', 'Part-time'],
    ['Contract', 'Contract'],
    ['Temporary', 'Temporary'],
    ['Internship', 'Internship'],
  ])('labels %s as %s', (value, expected) => {
    expect(label(value)).toBe(expected);
  });

  it('passes unknown values through unchanged', () => {
    expect(label('Apprenticeship')).toBe('Apprenticeship');
  });
});

describe('formatSalary', () => {
  // en-GB compact notation (CLDR) writes thousands with a lowercase k.
  it('formats a range compactly with an en dash', () => {
    expect(formatSalary(70_000, 85_000, 'GBP')).toBe('£70k–£85k');
  });

  it('formats an open-ended range from whichever bound is known', () => {
    expect(formatSalary(70_000, null, 'GBP')).toBe('£70k');
    expect(formatSalary(undefined, 85_000, 'GBP')).toBe('£85k');
  });

  it('defaults to pounds when the currency is missing', () => {
    expect(formatSalary(45_000, 55_000, null)).toBe('£45k–£55k');
  });

  it("uses the job's currency", () => {
    expect(formatSalary(120_000, 150_000, 'USD')).toBe('US$120k–US$150k');
    expect(formatSalary(60_000, null, 'EUR')).toBe('€60k');
  });

  it('returns null when there is no salary', () => {
    expect(formatSalary(null, null, 'GBP')).toBeNull();
    expect(formatSalary()).toBeNull();
  });
});
