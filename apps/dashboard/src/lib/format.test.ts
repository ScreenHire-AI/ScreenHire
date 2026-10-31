import { describe, expect, it } from 'vitest';
import { fileSize, formatSalary, initials, sourceLabel } from './format.js';

describe('formatSalary', () => {
  // The compact suffix's case comes from the runtime's locale data: "K" in Chromium, "k" in some Node builds.
  it('shows a range, an open-ended range and an hourly rate', () => {
    expect(formatSalary({ min: 70_000, max: 85_000, currency: 'GBP', period: 'Year' })).toMatch(/^£70k-£85k$/i);
    expect(formatSalary({ min: 30_000, max: null, currency: 'EUR', period: 'Year' })).toMatch(/^from €30k$/i);
    expect(formatSalary({ min: null, max: 14, currency: 'GBP', period: 'Hour' })).toBe('up to £14/hr');
  });
});

describe('initials', () => {
  it('takes the first letters of up to two words', () => {
    expect(initials('Casey Morgan')).toBe('CM');
    expect(initials('Riley')).toBe('R');
    expect(initials('Ana María de la Cruz')).toBe('AM');
  });
});

describe('fileSize', () => {
  it.each([
    [120, '1 kB'],
    [48_200, '48 kB'],
    [2_430_000, '2.4 MB'],
  ])('%i bytes is %s', (bytes, expected) => {
    expect(fileSize(bytes)).toBe(expected);
  });
});

describe('sourceLabel', () => {
  it('names the organization site the careers page was embedded in, or where else it came from', () => {
    expect(sourceLabel('CareersSite', 'https://www.example.com')).toBe('www.example.com');
    expect(sourceLabel('CareersSite', 'http://partner.localhost:8080')).toBe('partner.localhost:8080');
    expect(sourceLabel('CareersSite', null)).toBe('Careers page');
    expect(sourceLabel('Recruiter')).toBe('Recruiter');
  });
});
