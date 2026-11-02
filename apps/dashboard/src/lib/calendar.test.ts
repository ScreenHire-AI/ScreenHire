import { describe, expect, it } from 'vitest';
import { calendarFailure, calendarProblems, isCalendarOutcome } from './calendar.js';

describe('Calendar helpers', () => {
  it('says what to do when the calendar needs reconnecting', () => {
    expect(calendarProblems.NeedsReconnect).toMatch(/Connect it again/);
  });

  it('words each failed round trip, and nothing for a success or an unknown value', () => {
    expect(calendarFailure('denied')).toMatch(/cancelled/);
    expect(calendarFailure('expired')).toMatch(/too long/);
    expect(calendarFailure('failed')).toMatch(/could not be connected/);
    expect(calendarFailure('connected')).toBeUndefined();
    expect(calendarFailure('toString')).toBeUndefined();
    expect(calendarFailure(null)).toBeUndefined();
  });

  it('knows an outcome from any other value', () => {
    expect(isCalendarOutcome('connected')).toBe(true);
    expect(isCalendarOutcome('denied')).toBe(true);
    expect(isCalendarOutcome('nope')).toBe(false);
    expect(isCalendarOutcome(null)).toBe(false);
  });
});
