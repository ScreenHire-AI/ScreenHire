import type { Interviewer, TeamDetail } from '@screenhire/api-client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  canInterview,
  checkOption,
  defaultWindow,
  hoursProblem,
  hoursText,
  type OptionValues,
  scheduleRequest,
  scheduleSchema,
  stageDefaultPeople,
  weekdayHours,
  zonedTime,
} from '#lib/interview-options.js';
import { timeLabel } from '#lib/schedule-interview.js';

const valid: OptionValues = {
  option: 'OurTimes',
  interviewerUserIds: ['u1'],
  durationMinutes: 30,
  windowStart: '2026-11-03',
  windowEnd: '2026-11-16',
  timeZone: 'America/New_York',
  hours: weekdayHours(),
  useBlocks: false,
  proposed: ['', ''],
  extra: [],
};
const now = Date.parse('2026-11-02T12:00:00Z');

// The schema reads the real clock for "today", so the browser's clock stands at `now` too: the fixed days above stay in the
// future whatever day the suite runs.
beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(now);
});
afterEach(() => {
  vi.useRealTimers();
});

const problems = (patch: Partial<OptionValues>) => {
  const found: Record<string, string> = {};
  checkOption({ ...valid, ...patch }, (field, message) => (found[field] = message), now);
  return found;
};

const person = (userId: string, patch: Partial<Interviewer> = {}): Interviewer => ({
  userId,
  displayName: userId,
  calendarConnected: false,
  calendarProvider: null,
  hasHours: false,
  ...patch,
});

describe('the choices', () => {
  it('accepts a sound request for each way to pick', () => {
    expect(problems({})).toEqual({});
    expect(problems({ option: 'TheirTimes' })).toEqual({});
    expect(problems({ option: 'OurSuggestion', proposed: ['2026-11-04T10:00', ''] })).toEqual({});
    expect(scheduleSchema.safeParse(valid).success).toBe(true);
  });

  it('allows 90 days and refuses 91, in words, on the last day', () => {
    expect(problems({ windowStart: '2026-11-03', windowEnd: '2027-01-31' })).toEqual({});
    expect(problems({ windowStart: '2026-11-03', windowEnd: '2027-02-01' })).toEqual({ windowEnd: 'The window can be at most 90 days.' });
  });

  it('refuses a window that ends before it starts or has already ended', () => {
    expect(problems({ windowEnd: '2026-11-02' })).toEqual({ windowEnd: 'The last day has to be the first day or later.' });
    expect(problems({ windowStart: '2026-10-20', windowEnd: '2026-11-01' })).toEqual({ windowEnd: 'The last day has to be today or later.' });
    expect(problems({ windowStart: '2026-11-02', windowEnd: '2026-11-02' })).toEqual({});
  });

  it('asks for both days and a zone the browser knows', () => {
    expect(problems({ windowStart: '' })).toEqual({ windowStart: 'Pick the first day.' });
    expect(problems({ windowEnd: '' })).toEqual({ windowEnd: 'Pick the last day.' });
    expect(problems({ timeZone: '' }).timeZone).toBe('Choose a time zone.');
  });

  it('wants hours on at least one day that end after they start and hold the interview', () => {
    expect(problems({ hours: [] })).toEqual({ hours: 'Choose at least one day.' });
    expect(problems({ hours: [{ day: 'Tuesday', from: '17:00', to: '09:00' }] })).toEqual({ hours: 'Hours have to end after they start.' });
    expect(problems({ hours: [{ day: 'Tuesday', from: '09:00', to: '09:30' }], durationMinutes: 60 })).toEqual({ hours: 'The hours are shorter than the interview.' });
    expect(hoursProblem([{ day: 'Saturday', from: '10:00', to: '12:00' }], 60)).toBeUndefined();
  });

  it('wants one or two future times for we will suggest a time, and no days of its own', () => {
    const suggest = (proposed: string[]) => problems({ option: 'OurSuggestion', proposed, windowStart: '' });
    expect(suggest(['', ''])).toEqual({ proposed: 'Pick a time for them.' });
    expect(suggest(['2026-11-01T10:00', ''])).toEqual({ proposed: 'Each time has to be in the future.' });
    expect(suggest(['2026-11-04T10:00', '2026-11-04T10:00'])).toEqual({ proposed: 'Pick two different times.' });
    expect(suggest(['2026-11-04T10', ''])).toEqual({ proposed: 'Write each time in full.' });
  });

  it('holds one-off times to the days the candidate picks from', () => {
    expect(problems({ extra: ['2026-11-20T10:00'] })).toEqual({ extra: 'A one-off time has to be inside the days they pick from.' });
    expect(problems({ extra: ['2026-11-05T19:30'] })).toEqual({});
  });

  it('wants one to five interviewers and a length the API offers', () => {
    const result = (patch: Partial<OptionValues>) => scheduleSchema.safeParse({ ...valid, ...patch });
    expect(result({ interviewerUserIds: [] }).error?.issues[0]?.message).toBe('Choose who will interview.');
    expect(result({ interviewerUserIds: ['a', 'b', 'c', 'd', 'e'] }).success).toBe(true);
    expect(result({ interviewerUserIds: ['a', 'b', 'c', 'd', 'e', 'f'] }).error?.issues[0]?.message).toBe('Choose 1 to 5 interviewers.');
    expect(result({ durationMinutes: 40 }).error?.issues[0]?.message).toBe('Choose how long it lasts.');
  });
});

describe('what the API is sent', () => {
  it("sends pick from our times with its days, hours and one-off times in the chosen zone, not the browser's", () => {
    const request = scheduleRequest({ ...valid, extra: ['2026-11-05T19:30'], useBlocks: true }, now)!;
    expect(request).toMatchObject({
      option: 'OurTimes',
      windowStart: '2026-11-03T05:00:00.000Z',
      windowEnd: '2026-11-17T05:00:00.000Z',
      useBlocks: true,
      extraTimes: ['2026-11-06T00:30:00.000Z'],
    });
    expect(request.hours?.days).toHaveLength(5);
  });

  it('sends we will suggest a time with its times, open from today to the end of the last one', () => {
    const request = scheduleRequest({ ...valid, option: 'OurSuggestion', proposed: ['2026-11-06T09:00', '2026-11-04T15:30'] }, now)!;
    expect(request).toMatchObject({
      option: 'OurSuggestion',
      proposedTimes: ['2026-11-04T20:30:00.000Z', '2026-11-06T14:00:00.000Z'],
      windowStart: '2026-11-02T05:00:00.000Z',
      windowEnd: '2026-11-07T05:00:00.000Z',
    });
    expect(request.hours).toBeUndefined();
  });

  it('reads a picked wall-clock time in the zone across the end of daylight saving time', () => {
    // New York falls back on 2026-11-01 at 02:00: 01:30 happens twice and means the first (EDT); 03:00 is EST.
    expect(new Date(zonedTime('2026-11-01T01:30', 'America/New_York')!).toISOString()).toBe('2026-11-01T05:30:00.000Z');
    expect(new Date(zonedTime('2026-11-01T03:00', 'America/New_York')!).toISOString()).toBe('2026-11-01T08:00:00.000Z');
    expect(zonedTime('2026-11-01T25:00', 'UTC')).toBeNull();
  });
});

describe('who can interview, and the stage default', () => {
  it('needs a calendar or hours for times read from calendars, and nothing for the other ways', () => {
    const nobody = person('a');
    expect(canInterview(nobody, 'OurTimes', false)).toBe(false);
    expect(canInterview(person('b', { hasHours: true }), 'OurTimes', false)).toBe(true);
    expect(canInterview(person('c', { calendarConnected: true }), 'OurTimes', false)).toBe(true);
    expect(canInterview(nobody, 'OurTimes', true)).toBe(true);
    expect(canInterview(nobody, 'TheirTimes', false)).toBe(true);
    expect(canInterview(nobody, 'OurSuggestion', false)).toBe(true);
  });

  it("starts with the stage team's people who see the job, and names those who do not", () => {
    const team = {
      id: 'team-1',
      name: 'Kitchen',
      members: [
        { memberId: 'm1', userId: 'u1', email: 'a@example.com', displayName: 'Avery', roleName: 'Recruiter', seesAllJobs: false },
        { memberId: 'm2', userId: 'u2', email: 'r@example.com', displayName: 'Robin', roleName: 'Recruiter', seesAllJobs: false },
      ],
    } as unknown as TeamDetail;

    expect(stageDefaultPeople({ interviewerTeamId: 'team-1' }, [team], [person('u1')])).toEqual({ selected: ['u1'], notOnJob: ['Robin'], teamMissing: false });
    expect(stageDefaultPeople({ interviewerTeamId: 'gone' }, [team], [person('u1')]).teamMissing).toBe(true);
    expect(stageDefaultPeople({ interviewerUserIds: ['u1', 'u9'] }, undefined, [person('u1')])).toEqual({ selected: ['u1'], notOnJob: ['A former member'], teamMissing: false });
    expect(stageDefaultPeople(undefined, undefined, [person('u1')]).selected).toEqual([]);
  });

  it("opens the stage's days ahead from tomorrow", () => {
    expect(defaultWindow(now, 'UTC', 7)).toEqual({ windowStart: '2026-11-03', windowEnd: '2026-11-09' });
    expect(defaultWindow(now, 'UTC', null)).toEqual({ windowStart: '2026-11-03', windowEnd: '2026-11-16' });
  });

  it('writes hours as a week when every day is the same, and each day otherwise', () => {
    expect(hoursText(weekdayHours(), timeLabel)).toBe(`Mon to Fri, ${timeLabel('09:00')} to ${timeLabel('17:00')}`);
    expect(hoursText([{ day: 'Thursday', from: '14:00', to: '17:00' }, { day: 'Tuesday', from: '14:00', to: '17:00' }], timeLabel)).toBe(
      `Tue, Thu, ${timeLabel('14:00')} to ${timeLabel('17:00')}`,
    );
  });
});
