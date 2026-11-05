import { describe, expect, it } from 'vitest';
import { biggestDrop, changeText, counted, daysText, hireRate, oneDecimal, shareText, weekDateText, weekText } from '#lib/reports.js';

describe('report words', () => {
  it('gives a share in whole percents, and none of nothing', () => {
    expect(shareText(5, 8)).toBe('63%');
    expect(shareText(0, 4)).toBe('0%');
    expect(shareText(0, 0)).toBeNull();
  });

  it('rounds a median to whole days', () => {
    expect(daysText(9.5)).toBe('10 days');
    expect(daysText(1.2)).toBe('1 day');
  });

  it('counts with the right noun', () => {
    expect(counted(1, 'hire', 'hires')).toBe('1 hire');
    expect(counted(1284, 'hire', 'hires')).toBe('1,284 hires');
  });

  it('gives a hire rate, and none of no applications', () => {
    expect(hireRate(1, 4)).toBe('25%');
    expect(hireRate(0, 5)).toBe('0%');
    expect(hireRate(0, 0)).toBeNull();
  });

  it('names a week by its Monday', () => {
    expect(weekText('2026-11-02')).toMatch(/^(2 Nov|Nov 2)$/);
    expect(weekText('nonsense')).toBe('nonsense');
  });

  it('names a week in full for the table, so a year-long report tells its two Novembers apart', () => {
    expect(weekDateText('2026-11-02')).toMatch(/^(2 Nov 2026|Nov 2, 2026)$/);
    expect(weekDateText('2025-11-03')).toMatch(/^(3 Nov 2025|Nov 3, 2025)$/);
    expect(weekDateText('nonsense')).toBe('nonsense');
  });

  it('keeps one decimal of a median for the export', () => {
    expect(oneDecimal(17.25)).toBe(17.3);
    expect(oneDecimal(9)).toBe(9);
  });
});

describe('a headline number against the previous period', () => {
  it('says how many more or fewer, with the arrow following the number', () => {
    expect(changeText(40, 22)).toEqual({ text: '18 more than before', short: '18', direction: 'up' });
    expect(changeText(1, 5)).toEqual({ text: '4 fewer than before', short: '4', direction: 'down' });
    expect(changeText(1284, 4)).toEqual({ text: '1,280 more than before', short: '1,280', direction: 'up' });
    expect(changeText(7, 7)).toEqual({ text: 'No change', short: 'No change', direction: 'none' });
  });

  it('reads a median in days as faster or slower', () => {
    expect(changeText(14, 17, 'days')).toEqual({ text: '3 days faster', short: '3 days', direction: 'down' });
    expect(changeText(15, 14, 'days')).toEqual({ text: '1 day slower', short: '1 day', direction: 'up' });
    expect(changeText(14.4, 13.6, 'days')).toEqual({ text: 'No change', short: 'No change', direction: 'none' });
  });

  it('says nothing when there is nothing to compare', () => {
    expect(changeText(5, null)).toBeNull();
    expect(changeText(5, undefined)).toBeNull();
    expect(changeText(null, 5, 'days')).toBeNull();
  });
});

describe('the biggest drop in the funnel', () => {
  const row = (kind: string, applications: number, movedOn: number | null) => ({ kind, applications, movedOn });

  it('marks the stage the smallest share moved on from', () => {
    expect(biggestDrop([row('Applied', 10, 9), row('Screening', 9, 4), row('Interview', 4, 3), row('Hired', 3, null)])).toBe('Screening');
  });

  it('prefers the earlier row on a tie, and ignores rows with too few applications', () => {
    expect(biggestDrop([row('Applied', 10, 5), row('Screening', 4, 2), row('Offer', 2, 0), row('Hired', 1, null)])).toBe('Applied');
    expect(biggestDrop([row('Applied', 10, 10), row('Offer', 1, 0), row('Hired', 1, null)])).toBeNull();
  });

  it('marks nothing when everyone moved on', () => {
    expect(biggestDrop([row('Applied', 5, 5), row('Hired', 5, null)])).toBeNull();
    expect(biggestDrop([])).toBeNull();
  });
});
