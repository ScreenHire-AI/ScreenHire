import { describe, expect, it } from 'vitest';
import { customRangeProblem, dayText, defaultRange, parseDay, rangeInstants, rangeLabel, readRange, sameRange, writeRange } from '#lib/report-range.js';

// Local times throughout: the range is the person's own days, whatever zone the test runs in.
const now = new Date(2026, 9, 30, 15, 45);
const midnight = (year: number, month: number, day: number) => new Date(year, month - 1, day).toISOString();

describe('report ranges', () => {
  it('runs a preset from the local midnight that starts its first day to the one after today', () => {
    expect(rangeInstants({ kind: 'preset', days: 30 }, now)).toEqual({ from: midnight(2026, 10, 1), to: midnight(2026, 10, 31) });
    expect(rangeInstants({ kind: 'preset', days: 365 }, now)).toEqual({ from: midnight(2025, 10, 31), to: midnight(2026, 10, 31) });
  });

  it('includes both days of a custom range', () => {
    expect(rangeInstants({ kind: 'custom', start: '2026-01-01', end: '2026-03-31' }, now)).toEqual({
      from: midnight(2026, 1, 1),
      to: midnight(2026, 4, 1),
    });
  });

  it('reads the range from the address and writes it back, leaving the default out', () => {
    const url = new URL('https://dashboard.test/reports?job=a');
    writeRange(url, { kind: 'preset', days: 90 });
    expect(url.search).toBe('?job=a&range=90');
    expect(readRange(url.searchParams)).toEqual({ kind: 'preset', days: 90 });

    writeRange(url, { kind: 'custom', start: '2026-01-01', end: '2026-01-31' });
    expect(url.searchParams.get('range')).toBeNull();
    expect(readRange(url.searchParams)).toEqual({ kind: 'custom', start: '2026-01-01', end: '2026-01-31' });

    writeRange(url, defaultRange);
    expect(url.search).toBe('?job=a');
    expect(readRange(url.searchParams)).toEqual(defaultRange);
  });

  it('falls back to the last 30 days for a range that is not one', () => {
    for (const search of ['range=7', 'range=abc', 'from=2026-02-30&to=2026-03-01', 'from=2026-03-02&to=2026-03-01', 'from=2026-01-01']) {
      expect(readRange(new URLSearchParams(search)), search).toEqual(defaultRange);
    }
  });

  it('says in words what is wrong with a custom range', () => {
    expect(customRangeProblem('2026-01-01', '2026-01-01')).toBeNull();
    expect(customRangeProblem('', '2026-01-01')).toBe('Pick both dates.');
    expect(customRangeProblem('2026-02-01', '2026-01-01')).toBe('The range has to end on or after its first day.');
    expect(customRangeProblem('2020-01-01', '2026-01-01')).toBe('A report covers 5 years at most.');
    // 1826 days, both ends included: under the API's 1827 days even with a clock change inside the range.
    expect(customRangeProblem('2021-06-01', '2026-05-31')).toBeNull();
    expect(customRangeProblem('2021-06-01', '2026-06-01')).toBe('A report covers 5 years at most.');
  });

  it('reads and writes days as date fields hold them', () => {
    expect(parseDay('2026-10-30')?.getDate()).toBe(30);
    expect(parseDay('2026-13-01')).toBeNull();
    expect(dayText(new Date(2026, 0, 5))).toBe('2026-01-05');
  });

  it('names presets in words and compares choices', () => {
    expect(rangeLabel({ kind: 'preset', days: 90 })).toBe('Last 90 days');
    expect(rangeLabel({ kind: 'custom', start: '2026-10-01', end: '2026-10-30' })).toMatch(/Oct/);
    expect(sameRange(defaultRange, { kind: 'preset', days: 30 })).toBe(true);
    expect(sameRange(defaultRange, { kind: 'custom', start: '2026-10-01', end: '2026-10-30' })).toBe(false);
  });
});
