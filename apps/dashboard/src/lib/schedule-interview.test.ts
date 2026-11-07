import { describe, expect, it } from 'vitest';
import {
  addDays,
  checkSchedule,
  checkWindow,
  dayIn,
  daysCovered,
  defaultDays,
  durationLabel,
  newTimesSchema,
  scheduleSchema,
  timeLabel,
  timeOptions,
  timeZoneOptions,
  toggleInterviewer,
  windowInstants,
  zoneLabel,
  zonedMidnight,
} from '#lib/schedule-interview.js';

const valid = {
  interviewerUserIds: ['u1'],
  durationMinutes: 30,
  windowStart: '2026-11-03',
  windowEnd: '2026-11-16',
  timeZone: 'America/New_York',
  dayStart: '09:00',
  dayEnd: '17:00',
};
const now = Date.parse('2026-11-02T12:00:00Z');

const problems = (patch: Partial<typeof valid>) => {
  const found: Record<string, string> = {};
  checkSchedule({ ...valid, ...patch }, (field, message) => (found[field] = message), now);
  return found;
};

describe('window to instants', () => {
  it("starts a day at its midnight in the chosen zone, not the browser's", () => {
    expect(new Date(zonedMidnight('2026-10-31', 'America/New_York')!).toISOString()).toBe('2026-10-31T04:00:00.000Z');
    expect(new Date(zonedMidnight('2026-10-31', 'Asia/Kolkata')!).toISOString()).toBe('2026-10-30T18:30:00.000Z');
    expect(new Date(zonedMidnight('2026-10-31', 'UTC')!).toISOString()).toBe('2026-10-31T00:00:00.000Z');
  });

  it('ends the window at the start of the day after the last day, across the clock change that ends daylight saving time', () => {
    // New York falls back on 2026-11-01: the first day is in EDT (UTC-4), the day after the last in EST (UTC-5).
    expect(windowInstants('2026-10-31', '2026-11-02', 'America/New_York')).toEqual({
      windowStart: '2026-10-31T04:00:00.000Z',
      windowEnd: '2026-11-03T05:00:00.000Z',
    });
  });

  it('follows the clock change that starts daylight saving time, in the north and the south', () => {
    expect(windowInstants('2026-03-07', '2026-03-09', 'America/New_York')).toEqual({
      windowStart: '2026-03-07T05:00:00.000Z',
      windowEnd: '2026-03-10T04:00:00.000Z',
    });
    // Sydney springs forward on 2026-10-04: AEST (UTC+10) before, AEDT (UTC+11) after.
    expect(windowInstants('2026-10-03', '2026-10-05', 'Australia/Sydney')).toEqual({
      windowStart: '2026-10-02T14:00:00.000Z',
      windowEnd: '2026-10-05T13:00:00.000Z',
    });
  });

  it('holds a 90-day window that crosses a fall-back to the API 90 days of elapsed time', () => {
    // 90 local days from Aug 5 to Nov 2 is 90 days and an hour in New York; the API refuses anything over 90 days.
    const instants = windowInstants('2026-08-05', '2026-11-02', 'America/New_York')!;
    expect(daysCovered('2026-08-05', '2026-11-02')).toBe(90);
    expect(Date.parse(instants.windowEnd) - Date.parse(instants.windowStart)).toBe(90 * 86_400_000);
  });

  it('gives nothing for a day or a zone that is not real', () => {
    expect(windowInstants('2026-02-30', '2026-03-02', 'UTC')).toBeNull();
    expect(windowInstants('2026-03-01', '2026-03-02', 'Mars/Olympus')).toBeNull();
    expect(zonedMidnight('', 'UTC')).toBeNull();
  });

  it('counts days and moves between them without the browser zone', () => {
    expect(daysCovered('2026-11-03', '2026-11-03')).toBe(1);
    expect(daysCovered('2026-11-03', '2026-11-16')).toBe(14);
    expect(addDays('2026-12-31', 1)).toBe('2027-01-01');
    expect(addDays('2026-03-01', -1)).toBe('2026-02-28');
  });

  it("starts tomorrow and ends two weeks from today, by today's date in the zone", () => {
    const at = Date.parse('2026-10-20T12:00:00Z');
    expect(dayIn(at, 'UTC')).toBe('2026-10-20');
    expect(defaultDays(at, 'UTC')).toEqual({ windowStart: '2026-10-21', windowEnd: '2026-11-03' });
    // It is already the 21st in Auckland.
    expect(defaultDays(at, 'Pacific/Auckland')).toEqual({ windowStart: '2026-10-22', windowEnd: '2026-11-04' });
  });
});

describe('the choices', () => {
  it('accepts a sound request', () => {
    expect(problems({})).toEqual({});
    expect(scheduleSchema.safeParse(valid).success).toBe(true);
  });

  it('allows 90 days and refuses 91, in words, on the last day', () => {
    expect(problems({ windowStart: '2026-11-03', windowEnd: '2027-01-31' })).toEqual({});
    expect(problems({ windowStart: '2026-11-03', windowEnd: '2027-02-01' })).toEqual({ windowEnd: 'The window can be at most 90 days.' });
  });

  it('refuses a window that ends before it starts or has already ended', () => {
    expect(problems({ windowEnd: '2026-11-02' })).toEqual({ windowEnd: 'The last day has to be the first day or later.' });
    expect(problems({ windowStart: '2026-10-20', windowEnd: '2026-11-01' })).toEqual({ windowEnd: 'The last day has to be today or later.' });
    // The same day is still open: it ends at midnight.
    expect(problems({ windowStart: '2026-11-02', windowEnd: '2026-11-02' })).toEqual({});
  });

  it('asks for both days and a zone the browser knows', () => {
    expect(problems({ windowStart: '' })).toEqual({ windowStart: 'Pick the first day.' });
    expect(problems({ windowEnd: '' })).toEqual({ windowEnd: 'Pick the last day.' });
    expect(problems({ timeZone: '' })).toEqual({ timeZone: 'Choose a time zone.' });
  });

  it('wants working hours that end after they start and hold the interview', () => {
    expect(problems({ dayStart: '17:00', dayEnd: '09:00' })).toEqual({ dayEnd: 'Working hours have to end after they start.' });
    expect(problems({ dayStart: '09:00', dayEnd: '09:30', durationMinutes: 60 })).toEqual({ dayEnd: 'Working hours are shorter than the interview.' });
    expect(problems({ dayStart: '09:00', dayEnd: '10:00', durationMinutes: 60 })).toEqual({});
  });

  it('wants one to five interviewers and a length the API offers', () => {
    const result = (patch: Partial<typeof valid>) => scheduleSchema.safeParse({ ...valid, ...patch });
    expect(result({ interviewerUserIds: [] }).error?.issues[0]?.message).toBe('Choose who will interview.');
    expect(result({ interviewerUserIds: ['a', 'b', 'c', 'd', 'e'] }).success).toBe(true);
    expect(result({ interviewerUserIds: ['a', 'b', 'c', 'd', 'e', 'f'] }).error?.issues[0]?.message).toBe('Choose 1 to 5 interviewers.');
    expect(result({ durationMinutes: 40 }).error?.issues[0]?.message).toBe('Choose how long it lasts.');
  });
});

describe('the days of new times', () => {
  const issues = (windowStart: string, windowEnd: string, zone = 'America/New_York') => {
    const found: Record<string, string> = {};
    checkWindow({ windowStart, windowEnd }, zone, (field, message) => (found[field] = message), now);
    return found;
  };

  it('holds a window to the same rules as a new interview, on the same fields', () => {
    expect(issues('2026-11-03', '2026-11-16')).toEqual({});
    expect(issues('2026-11-03', '2027-01-31')).toEqual({});
    expect(issues('2026-11-03', '2027-02-01')).toEqual({ windowEnd: 'The window can be at most 90 days.' });
    expect(issues('2026-11-03', '2026-11-02')).toEqual({ windowEnd: 'The last day has to be the first day or later.' });
    expect(issues('2026-10-20', '2026-11-01')).toEqual({ windowEnd: 'The last day has to be today or later.' });
    expect(issues('', '2026-11-16')).toEqual({ windowStart: 'Pick the first day.' });
    expect(issues('2026-11-03', '')).toEqual({ windowEnd: 'Pick the last day.' });
  });

  it('is checked in the interview zone and reports the same problems through the schema', () => {
    // The last day is still open: 2026-11-02 ends at midnight in New York (05:00Z on the 3rd), after the 12:00Z test clock.
    expect(issues('2026-10-30', '2026-11-02', 'America/New_York')).toEqual({});
    expect(newTimesSchema('America/New_York').safeParse({ windowStart: '2026-11-03', windowEnd: '2026-11-16' }).success).toBe(true);
    const refused = newTimesSchema('America/New_York').safeParse({ windowStart: '2026-11-03', windowEnd: '2027-02-01' });
    expect(refused.error?.issues.map((issue) => [issue.path[0], issue.message])).toEqual([['windowEnd', 'The window can be at most 90 days.']]);
  });
});

describe('interviewers', () => {
  it('makes the first person ticked the organizer, whatever the order of the list', () => {
    let selected: string[] = [];
    selected = toggleInterviewer(selected, 'robin', true);
    selected = toggleInterviewer(selected, 'avery', true);
    expect(selected).toEqual(['robin', 'avery']);
    expect(selected[0]).toBe('robin');
  });

  it('hands the organizer to the next person when the first is unticked, and puts a person ticked again last', () => {
    expect(toggleInterviewer(['robin', 'avery'], 'robin', false)).toEqual(['avery']);
    expect(toggleInterviewer(['avery'], 'robin', true)).toEqual(['avery', 'robin']);
    expect(toggleInterviewer(['robin', 'avery'], 'robin', true)).toEqual(['avery', 'robin']);
  });
});

describe('what the selects offer', () => {
  it('has a half hour step for the whole day', () => {
    expect(timeOptions).toHaveLength(48);
    expect(timeOptions[0]!.value).toBe('00:00');
    expect(timeOptions[18]!.value).toBe('09:00');
    expect(timeOptions[47]!.value).toBe('23:30');
  });

  it('names zones by place and common name with the IANA id as the value, and keeps the current one', () => {
    const options = timeZoneOptions('Asia/Kolkata');
    const newYork = options.find((option) => option.value === 'America/New_York');
    expect(newYork?.label).toMatch(/^New York \(.+\)$/);
    expect(options.some((option) => option.value === 'UTC')).toBe(true);
    expect(options.some((option) => option.value === 'Asia/Kolkata')).toBe(true);
  });

  it('words a zone and a time of day as the selects do', () => {
    expect(zoneLabel('America/New_York')).toBe('New York (Eastern Time)');
    expect(zoneLabel('UTC')).toMatch(/^UTC/);
    expect(zoneLabel('Not/A_Zone')).toBe('A Zone');
    expect(timeLabel('09:00')).toBe('9:00 AM');
    expect(timeLabel('09:15')).toBe('09:15');
  });

  it('words the lengths', () => {
    expect([15, 30, 45, 60, 90, 120].map(durationLabel)).toEqual(['15 minutes', '30 minutes', '45 minutes', '1 hour', '1 hour 30 minutes', '2 hours']);
  });
});
