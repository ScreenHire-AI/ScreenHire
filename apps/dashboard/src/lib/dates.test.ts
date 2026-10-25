import { describe, expect, it } from 'vitest';
import { dayKey, dayLabel, eta, formatDate, formatDateTime, timeAgo, timeUntil } from './dates.js';

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

describe('timeUntil', () => {
  it('says the time in words for a sentence, in the browser locale', () => {
    expect(timeUntil(new Date(now + 86_400_000).toISOString(), now, 'long')).toBe('1 day');
    expect(timeUntil(new Date(now + 20_000).toISOString(), now, 'long')).toBe('20 seconds');
  });
});

describe('dayLabel', () => {
  it.each([
    ['2026-10-20T00:30:00Z', 'Today'],
    ['2026-10-19T23:59:00Z', 'Yesterday'],
    ['2026-10-17T09:00:00Z', 'Oct 17'],
    ['2025-12-31T09:00:00Z', 'Dec 31, 2025'],
  ])('%s is headed "%s"', (iso, expected) => {
    expect(dayLabel(iso, now)).toBe(expected);
  });

  it('keys an instant by its local day', () => {
    expect(dayKey('2026-10-20T23:59:00Z')).toBe('2026-10-20');
    expect(dayKey('2026-10-21T00:00:00Z')).toBe('2026-10-21');
  });
});
