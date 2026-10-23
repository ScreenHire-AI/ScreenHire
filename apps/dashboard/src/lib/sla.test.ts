import { describe, expect, it } from 'vitest';
import { timeSince } from './dates.js';
import { savedViews } from './saved-views.js';
import { isOverdue, overdueLabel } from './sla.js';
import { applications } from '../test-support/fixtures.js';

const now = Date.parse('2026-10-20T12:00:00Z');
const before = (ms: number) => new Date(now - ms).toISOString();

describe('overdue', () => {
  it('starts when the time limit passes and never without one', () => {
    expect(isOverdue({ slaDueAt: before(1) }, now)).toBe(true);
    expect(isOverdue({ slaDueAt: before(0) }, now)).toBe(true);
    expect(isOverdue({ slaDueAt: before(-60_000) }, now)).toBe(false);
    expect(isOverdue({ slaDueAt: null }, now)).toBe(false);
    expect(isOverdue({}, now)).toBe(false);
  });

  it.each([
    [10_000, 'Overdue by 1 minute'],
    [5 * 60_000, 'Overdue by 5 minutes'],
    [5 * 3_600_000 + 59 * 60_000, 'Overdue by 5 hours'],
    [2 * 86_400_000 + 23 * 3_600_000, 'Overdue by 2 days'],
  ])('%i ms past the limit reads "%s"', (elapsed, expected) => {
    expect(overdueLabel(before(elapsed), now)).toBe(expected);
  });

  it('counts whole units since an instant', () => {
    expect(timeSince(before(86_400_000), now)).toBe('1 day');
    expect(timeSince(before(-5_000), now)).toBe('1 minute');
  });

  it('has a saved view, marked as a status, that finds the overdue applications', () => {
    const view = savedViews.find((candidate) => candidate.id === 'overdue')!;
    const rows = [
      { ...applications[0]!, slaDueAt: before(3_600_000) },
      { ...applications[1]!, slaDueAt: before(-3_600_000) },
      { ...applications[2]!, slaDueAt: null },
    ];

    expect(view.label).toBe('Overdue');
    expect(view.mark).toEqual({ shape: 'diamond', tone: 'review' });
    expect(rows.filter((row) => view.matches(row, now)).map((row) => row.id)).toEqual([applications[0]!.id]);
  });
});
