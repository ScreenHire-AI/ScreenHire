import { afterEach, describe, expect, it } from 'vitest';
import { configureDates, dateSettings } from './date-settings.svelte.js';
import { dayKey, dayLabel, formatDate, formatDayMonth } from './dates.js';

describe("the person's date settings", () => {
  afterEach(() => configureDates({}));

  it('writes dates in the chosen format and time zone', () => {
    // 23:30 UTC on the 21st is the 22nd in Tokyo.
    const at = '2026-11-21T23:30:00Z';
    configureDates({ timeZone: 'Asia/Tokyo', dateFormat: 'Iso' });
    expect(formatDate(at)).toBe('2026-11-22');

    configureDates({ timeZone: 'UTC', dateFormat: 'DayMonthYear' });
    expect(formatDate(at)).toBe('21 Nov 2026');

    configureDates({ timeZone: 'UTC', dateFormat: 'MonthDayYear' });
    expect(formatDate(at)).toBe('Nov 21, 2026');
  });

  it('writes the day and month in the chosen language', () => {
    configureDates({ language: 'en-GB', timeZone: 'UTC' });
    expect(formatDayMonth('2026-11-21T12:00:00Z')).toBe('21 November');
  });

  it("groups by the person's day, so a heading agrees with the dates under it", () => {
    // 23:30 UTC on the 21st is already the 22nd in Tokyo, where it is then 08:30 on the 22nd.
    configureDates({ timeZone: 'Asia/Tokyo' });
    expect(dayKey('2026-11-21T23:30:00Z')).toBe('2026-11-22');
    expect(dayLabel('2026-11-21T23:30:00Z', Date.parse('2026-11-21T23:45:00Z'))).toBe('Today');
    expect(dayLabel('2026-11-21T12:00:00Z', Date.parse('2026-11-21T23:45:00Z'))).toBe('Yesterday');

    configureDates({ timeZone: 'UTC' });
    expect(dayKey('2026-11-21T23:30:00Z')).toBe('2026-11-21');
  });

  it('changes nothing when the settings are the same', () => {
    configureDates({ timeZone: 'UTC' });
    const version = dateSettings.version;
    configureDates({ timeZone: 'UTC' });
    expect(dateSettings.version).toBe(version);
  });

  it('falls back to the browser for a time zone it does not know', () => {
    configureDates({ timeZone: 'Not/AZone', dateFormat: 'Iso' });
    expect(formatDate('2026-11-21T12:00:00Z')).toMatch(/^2026-11-2\d$/);
  });
});
