import { describe, expect, it } from 'vitest';
import { eta, formatSalary, initials } from './format.js';

const now = Date.parse('2026-10-20T12:00:00Z');

describe('eta', () => {
  it.each([
    [4_000, '~4s'],
    [5 * 60_000, '~5m'],
    [4 * 3_600_000, '~4h'],
    [86_400_000 + 300_000, '~1d'],
  ])('%i ms from now is %s', (remaining, expected) => {
    expect(eta(new Date(now + remaining).toISOString(), now)).toBe(expected);
  });
});

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
