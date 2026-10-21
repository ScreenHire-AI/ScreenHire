import { describe, expect, it } from 'vitest';
import { formatDate, formatDateTime, timeAgo } from './dates.js';

// jsdom's browser languages are US English, and vitest.config.ts pins the timezone to UTC.
const now = Date.parse('2026-10-20T12:00:00Z');
const before = (ms: number) => new Date(now - ms).toISOString();

describe('timeAgo', () => {
  it.each([
    [30_000, 'now'],
    [5 * 60_000, '5 minutes ago'],
    [3 * 3_600_000, '3 hours ago'],
    [86_400_000, 'yesterday'],
    [6 * 86_400_000, '6 days ago'],
    [7 * 86_400_000, 'Oct 13, 2026'],
    [17 * 86_400_000, 'Oct 3, 2026'],
  ])('%i ms ago reads "%s"', (elapsed, expected) => {
    expect(timeAgo(before(elapsed), now)).toBe(expected);
  });

  it('treats an instant ahead of the clock as now', () => {
    expect(timeAgo(new Date(now + 5_000).toISOString(), now)).toBe('now');
  });
});

describe('formatDate and formatDateTime', () => {
  it('show the medium date, with the time for the hover title', () => {
    expect(formatDate('2026-10-20T14:30:00Z')).toBe('Oct 20, 2026');
    expect(formatDateTime('2026-10-20T14:30:00Z')).toMatch(/^Oct 20, 2026,? (at )?2:30\sPM$/);
  });
});
