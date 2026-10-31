import { describe, expect, it } from 'vitest';
import { counted, daysText, shareText, stageKindLabel } from '#lib/reports.js';

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

  it('names a funnel row by its stage kind', () => {
    expect(stageKindLabel('Interview')).toBe('Interview');
  });
});
