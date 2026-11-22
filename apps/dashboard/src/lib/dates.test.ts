import { afterEach, describe, expect, it } from 'vitest';
import { configureDates } from '#lib/date-settings.svelte.js';
import { calendarDaysUntil, dayKey, dayLabel, formatDate, formatDateTime, formatDayMonth, shortDate, timeAgo, timeUntil } from './dates.js';

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

describe('formatDayMonth', () => {
  it('names the day and the month in full, with no year, in the browser locale (US English here)', () => {
    expect(formatDayMonth('2026-12-05T09:00:00Z')).toBe('December 5');
    expect(formatDayMonth('2027-01-11T23:30:00Z')).toBe('January 11');
  });
});

describe('shortDate', () => {
  it('leaves the year out in the current year and keeps it for another year', () => {
    expect(shortDate('2026-10-31T09:00:00Z', now)).toBe('Oct 31');
    expect(shortDate('2025-10-31T09:00:00Z', now)).toBe('Oct 31, 2025');
  });
});

describe('formatDate and formatDateTime', () => {
  it('show the medium date, with the time for the hover title', () => {
    expect(formatDate('2026-10-20T14:30:00Z')).toBe('Oct 20, 2026');
    expect(formatDateTime('2026-10-20T14:30:00Z')).toMatch(/^Oct 20, 2026,? (at )?2:30\sPM$/);
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
    ['2026-10-21T00:00:30Z', 'Today'],
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

// The trial's days left on Billing's plan card and in the bell: calendar days in the person's zone, which can differ from the
// browser's (UTC here).
describe('calendarDaysUntil', () => {
  afterEach(() => configureDates({}));
  const evening = Date.parse('2026-11-30T20:00:00Z');

  it('counts whole days in the browser zone while the person has none', () => {
    expect(calendarDaysUntil('2026-12-01T02:00:00Z', evening)).toBe(1);
    expect(calendarDaysUntil('2026-12-09T12:00:00Z', evening)).toBe(9);
    expect(calendarDaysUntil('2026-11-30T23:59:00Z', evening)).toBe(0);
    expect(calendarDaysUntil('2026-11-29T12:00:00Z', evening)).toBe(-1);
  });

  it("counts in the person's own zone, where the same two instants can fall on one day", () => {
    configureDates({ timeZone: 'Pacific/Auckland' });
    expect(calendarDaysUntil('2026-12-01T02:00:00Z', evening)).toBe(0);
    configureDates({ timeZone: 'America/Los_Angeles' });
    expect(calendarDaysUntil('2026-12-01T02:00:00Z', evening)).toBe(0);
    expect(calendarDaysUntil('2026-12-01T09:00:00Z', evening)).toBe(1);
  });

  it('is not shifted by a daylight saving change between the two days', () => {
    configureDates({ timeZone: 'America/New_York' });
    // Summer time ends on November 1 2026: noon on October 31 to noon on November 2 is two days, though 49 hours apart.
    expect(calendarDaysUntil('2026-11-02T17:00:00Z', Date.parse('2026-10-31T16:00:00Z'))).toBe(2);
  });
});
