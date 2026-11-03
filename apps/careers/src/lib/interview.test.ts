import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  changeInterview,
  DAYS_PER_SCREEN,
  dayKey,
  describeBooked,
  durationText,
  displayZone,
  groupByDay,
  hoursText,
  instantFrom,
  interviewPath,
  isToken,
  noticeFor,
  outcomeOf,
  screenOf,
  shownOutcome,
  timeRange,
  validZone,
  zoneLabel,
  zoneName,
  zoneOptions,
  type SlotDay,
} from './interview';

const token = 'CfDJ8Abc_def-123456789xyz';

describe('interview links', () => {
  it('takes only base64url tokens, so a crafted link cannot change the API path', () => {
    expect(isToken(token)).toBe(true);
    expect(isToken(`${token}/cancel`)).toBe(false);
    expect(isToken(`${token}?x=1`)).toBe(false);
    expect(isToken('short')).toBe(false);
    expect(isToken(undefined)).toBe(false);
  });
});

describe('time zones', () => {
  it('knows IANA zones and refuses anything else', () => {
    expect(validZone('Asia/Kolkata')).toBe('Asia/Kolkata');
    expect(validZone('UTC')).toBe('UTC');
    expect(validZone('America/Argentina/Buenos_Aires')).toBe('America/Argentina/Buenos_Aires');
    expect(validZone('Mars/Olympus_Mons')).toBeNull();
    expect(validZone('<script>')).toBeNull();
    expect(validZone('')).toBeNull();
    expect(validZone(null)).toBeNull();
    expect(validZone('A'.repeat(100))).toBeNull();
  });

  it('writes times in UTC when the zone is not one the runtime knows', () => {
    expect(displayZone('Europe/Berlin')).toBe('Europe/Berlin');
    expect(displayZone('Nowhere/Land')).toBe('UTC');
    expect(displayZone(undefined)).toBe('UTC');
  });

  it('offers the interview zone first, the common ones, and the zone being shown, each once', () => {
    const at = new Date('2026-03-03T12:00:00Z');
    const options = zoneOptions('Europe/Lisbon', 'Asia/Kolkata', at);
    expect(options[0]).toMatchObject({ value: 'Europe/Lisbon', selected: false });
    expect(options.filter((option) => option.value === 'Asia/Kolkata')).toHaveLength(1);
    expect(options.find((option) => option.selected)?.value).toBe('Asia/Kolkata');
    expect(new Set(options.map((option) => option.value)).size).toBe(options.length);

    const unusual = zoneOptions('America/New_York', 'Pacific/Fiji', at);
    expect(unusual.at(-1)).toMatchObject({ value: 'Pacific/Fiji', selected: true });
    expect(unusual.filter((option) => option.value === 'America/New_York')).toHaveLength(1);
  });

  it('labels a zone by its place and its offset from UTC at that time', () => {
    expect(zoneLabel('Asia/Kolkata', new Date('2026-03-03T12:00:00Z'))).toBe('Kolkata (GMT+5:30)');
    expect(zoneLabel('America/New_York', new Date('2026-01-15T12:00:00Z'))).toBe('New York (GMT-5)');
    expect(zoneLabel('America/New_York', new Date('2026-07-15T12:00:00Z'))).toBe('New York (GMT-4)');
    expect(zoneLabel('UTC', new Date('2026-07-15T12:00:00Z'))).toBe('UTC');
  });

  it('names a zone the same way in winter and summer', () => {
    expect(zoneName(new Date('2026-01-15T12:00:00Z'), 'America/New_York')).toContain('Eastern');
    expect(zoneName(new Date('2026-07-15T12:00:00Z'), 'America/New_York')).toContain('Eastern');
    expect(zoneName(new Date('2026-07-15T12:00:00Z'), 'Asia/Kolkata')).toContain('India');
  });
});

describe('the time that is posted', () => {
  it('takes an ISO instant with any offset and posts it as UTC', () => {
    expect(instantFrom('2026-03-03T09:00:00+00:00')).toBe('2026-03-03T09:00:00.000Z');
    expect(instantFrom('2026-03-03T14:30:00+05:30')).toBe('2026-03-03T09:00:00.000Z');
  });

  it('refuses anything else', () => {
    expect(instantFrom('soon')).toBeNull();
    expect(instantFrom('2026-13-45T99:00:00Z')).toBeNull();
    expect(instantFrom('')).toBeNull();
    expect(instantFrom(undefined)).toBeNull();
  });
});

describe('writing a time', () => {
  it('writes the range in the zone, on a 12-hour clock, with midnight as 12:00 AM', () => {
    expect(timeRange(new Date('2026-03-03T10:00:00Z'), 30, 'UTC')).toBe('10:00 AM to 10:30 AM');
    expect(timeRange(new Date('2026-03-03T10:00:00Z'), 45, 'UTC')).toBe('10:00 AM to 10:45 AM');
    expect(timeRange(new Date('2026-03-03T23:30:00Z'), 60, 'UTC')).toBe('11:30 PM to 12:30 AM');
    expect(timeRange(new Date('2026-03-03T00:00:00Z'), 30, 'UTC')).toBe('12:00 AM to 12:30 AM');
  });

  it('keeps a half-hour zone on the half hour', () => {
    expect(timeRange(new Date('2026-03-03T09:00:00Z'), 30, 'Asia/Kolkata')).toBe('2:30 PM to 3:00 PM');
    expect(timeRange(new Date('2026-03-03T09:30:00Z'), 30, 'Asia/Kolkata')).toBe('3:00 PM to 3:30 PM');
  });

  it('puts an instant on the local day of the zone', () => {
    expect(dayKey(new Date('2026-03-03T20:00:00Z'), 'UTC')).toBe('2026-03-03');
    expect(dayKey(new Date('2026-03-03T20:00:00Z'), 'Asia/Kolkata')).toBe('2026-03-04');
    expect(dayKey(new Date('2026-03-03T03:00:00Z'), 'America/Los_Angeles')).toBe('2026-03-02');
  });

  it('describes a booked time with its day, year, range and zone', () => {
    expect(describeBooked('2026-03-03T09:00:00Z', 30, 'Asia/Kolkata')).toEqual({
      day: 'Tuesday, March 3, 2026',
      range: '2:30 PM to 3:00 PM',
      zone: expect.stringContaining('India') as string,
    });
  });
});

describe('lengths', () => {
  it('writes a length in words', () => {
    expect(durationText(30)).toBe('30 minutes');
    expect(durationText(15)).toBe('15 minutes');
    expect(durationText(60)).toBe('1 hour');
    expect(durationText(90)).toBe('1 hour 30 minutes');
    expect(durationText(120)).toBe('2 hours');
    expect(hoursText(24)).toBe('24 hours');
    expect(hoursText(1)).toBe('1 hour');
  });
});

describe('grouping times by day', () => {
  it('groups by the local day, earliest first, ignoring repeats and junk', () => {
    const days = groupByDay(['2026-03-04T10:00:00Z', '2026-03-03T09:30:00Z', '2026-03-03T09:00:00Z', '2026-03-03T09:00:00Z', 'not a time'], 'UTC');
    expect(days).toEqual([
      {
        key: '2026-03-03',
        heading: 'Tuesday, March 3',
        slots: [
          { value: '2026-03-03T09:00:00Z', label: '9:00 AM' },
          { value: '2026-03-03T09:30:00Z', label: '9:30 AM' },
        ],
      },
      { key: '2026-03-04', heading: 'Wednesday, March 4', slots: [{ value: '2026-03-04T10:00:00Z', label: '10:00 AM' }] },
    ]);
  });

  it('moves a late slot to the next local day when the zone is ahead of UTC', () => {
    const days = groupByDay(['2026-03-03T09:00:00Z', '2026-03-03T20:00:00Z'], 'Asia/Kolkata');
    expect(days.map((day) => [day.key, day.slots.map((slot) => slot.label)])).toEqual([
      ['2026-03-03', ['2:30 PM']],
      ['2026-03-04', ['1:30 AM']],
    ]);
    expect(days[1]!.heading).toBe('Wednesday, March 4');
  });

  it('moves an early slot to the previous local day when the zone is behind UTC', () => {
    const days = groupByDay(['2026-03-03T03:00:00Z', '2026-03-03T17:00:00Z'], 'America/Los_Angeles');
    expect(days.map((day) => [day.key, day.slots.map((slot) => slot.label)])).toEqual([
      ['2026-03-02', ['7:00 PM']],
      ['2026-03-03', ['9:00 AM']],
    ]);
  });

  it('writes the day daylight saving time starts as it is on the wall, with the hour that does not exist skipped', () => {
    // New York, Sunday March 8 2026: clocks go from 02:00 to 03:00, so 01:30 EST ends at 03:00 EDT after 30 minutes.
    const days = groupByDay(['2026-03-08T05:00:00Z', '2026-03-08T06:30:00Z', '2026-03-08T07:00:00Z', '2026-03-08T14:00:00Z'], 'America/New_York');
    expect(days).toHaveLength(1);
    expect(days[0]!.heading).toBe('Sunday, March 8');
    expect(days[0]!.slots.map((slot) => slot.label)).toEqual(['12:00 AM', '1:30 AM', '3:00 AM', '10:00 AM']);
  });

  it('keeps the same local hour either side of the change', () => {
    const days = groupByDay(['2026-03-06T14:00:00Z', '2026-03-09T13:00:00Z'], 'America/New_York');
    expect(days.map((day) => [day.key, day.slots[0]!.label])).toEqual([
      ['2026-03-06', '9:00 AM'],
      ['2026-03-09', '9:00 AM'],
    ]);
  });

  it('adds the year to days that fall in another year than the first', () => {
    const days = groupByDay(['2026-12-31T10:00:00Z', '2027-01-02T10:00:00Z'], 'UTC');
    expect(days.map((day) => day.heading)).toEqual(['Thursday, December 31', 'Saturday, January 2, 2027']);
  });

  it('offers nothing for no times', () => {
    expect(groupByDay([], 'UTC')).toEqual([]);
  });
});

describe('screens of days', () => {
  const days: SlotDay[] = Array.from({ length: 7 }, (_, index) => ({
    key: `2026-04-${String(index + 1).padStart(2, '0')}`,
    heading: `Day ${index + 1}`,
    slots: [],
  }));

  it('shows the first days and says where the next screen starts', () => {
    const screen = screenOf(days, null);
    expect(screen.days).toHaveLength(DAYS_PER_SCREEN);
    expect(screen.next).toBe('2026-04-06');
    expect(screen.later).toBe(false);
  });

  it('starts at the first day on or after the one asked for', () => {
    const screen = screenOf(days, '2026-04-06');
    expect(screen.days.map((day) => day.key)).toEqual(['2026-04-06', '2026-04-07']);
    expect(screen.next).toBeNull();
    expect(screen.later).toBe(true);
    expect(screenOf(days, '2026-04-02').days[0]!.key).toBe('2026-04-02');
  });

  it('falls back to the first screen for a day that is past the end or not a day', () => {
    expect(screenOf(days, '2027-01-01').days[0]!.key).toBe('2026-04-01');
    expect(screenOf(days, 'tomorrow').days[0]!.key).toBe('2026-04-01');
    expect(screenOf([], null)).toEqual({ days: [], next: null, later: false });
  });
});

describe('the address of the page', () => {
  it('carries the zone, the picker, the screen and the outcome, and nothing else', () => {
    expect(interviewPath('/acme', token)).toBe(`/acme/interviews/${token}`);
    expect(interviewPath('', token, { tz: 'Asia/Kolkata', change: true, from: '2026-03-10', outcome: 'slot-taken' })).toBe(
      `/interviews/${token}?tz=Asia%2FKolkata&change=1&from=2026-03-10&outcome=slot-taken`,
    );
    expect(interviewPath('/acme', token, { tz: null, change: false, outcome: null })).toBe(`/acme/interviews/${token}`);
  });
});

describe('what the page says', () => {
  it('shows only outcomes it knows', () => {
    expect(shownOutcome('booked')).toBe('booked');
    expect(shownOutcome('slot-taken')).toBe('slot-taken');
    expect(shownOutcome('<b>hi</b>')).toBeNull();
    expect(shownOutcome('closed')).toBeNull();
    expect(shownOutcome(null)).toBeNull();
  });

  it('has plain words for each, and calm ones when nothing is wrong', () => {
    expect(noticeFor('booked')).toMatchObject({ kind: 'success' });
    expect(noticeFor('cancelled')).toBeNull();
    expect(noticeFor('slot-taken')).toMatchObject({ kind: 'problem', message: 'That time was just taken. Choose another.' });
    expect(noticeFor('closed')).toBeNull();
    expect(noticeFor(null)).toBeNull();
    for (const outcome of ['booked', 'rescheduled', 'slot-taken', 'too-late', 'already-booked', 'not-booked', 'no-choice', 'unavailable', 'busy', 'error'] as const) {
      const message = noticeFor(outcome)!.message;
      expect(message).not.toMatch(/[–—‘’“”]/);
    }
  });
});

describe('what an answer from the API means', () => {
  it('maps a success to the action that was taken', () => {
    expect(outcomeOf('book', 204)).toBe('booked');
    expect(outcomeOf('reschedule', 204)).toBe('rescheduled');
    expect(outcomeOf('cancel', 204)).toBe('cancelled');
  });

  it('maps the statuses', () => {
    expect(outcomeOf('book', 404)).toBe('not-found');
    expect(outcomeOf('book', 410, { type: 'expired' })).toBe('closed');
    expect(outcomeOf('book', 429)).toBe('busy');
    expect(outcomeOf('book', 503, { type: 'calendar_unavailable' })).toBe('unavailable');
    expect(outcomeOf('book', 500)).toBe('error');
  });

  it('maps a conflict by the code the API gives', () => {
    expect(outcomeOf('book', 409, { type: 'slot_taken' })).toBe('slot-taken');
    expect(outcomeOf('reschedule', 409, { type: 'too_late' })).toBe('too-late');
    expect(outcomeOf('book', 409, { type: 'already_booked' })).toBe('already-booked');
    expect(outcomeOf('reschedule', 409, { type: 'not_booked' })).toBe('not-booked');
    expect(outcomeOf('book', 409, { type: 'https://example.org/problems/slot_taken' })).toBe('slot-taken');
    expect(outcomeOf('book', 409, { type: 'something_new' })).toBe('error');
    expect(outcomeOf('book', 409, undefined)).toBe('error');
  });
});

describe('changing an interview', () => {
  afterEach(() => vi.unstubAllGlobals());

  const json = (status: number, body: unknown = null) =>
    new Response(body === null ? null : JSON.stringify(body), { status, headers: { 'Content-Type': 'application/problem+json' } });

  const form = (entries: Record<string, string>): FormData => {
    const data = new FormData();
    for (const [name, value] of Object.entries(entries)) data.append(name, value);
    return data;
  };

  const posted = { organization: 'acme', token, start: '2026-03-03T09:00:00+00:00', tz: 'Asia/Kolkata' };

  it('books the time and sends the start as an instant, with the candidate address and nothing else', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(json(204));
    vi.stubGlobal('fetch', fetch);

    const result = await changeInterview('book', form(posted), '192.0.2.10');

    expect(result).toEqual({ outcome: 'booked', tz: 'Asia/Kolkata', change: false });
    const [url, init] = fetch.mock.calls[0]!;
    expect(String(url)).toContain(`/v1/public/organizations/acme/interviews/${token}/book`);
    expect((init as RequestInit).method).toBe('POST');
    expect(JSON.parse(String((init as RequestInit).body))).toEqual({ start: '2026-03-03T09:00:00.000Z' });
    expect(new Headers((init as RequestInit).headers).get('X-Forwarded-For')).toBe('192.0.2.10');
  });

  it('moves a booked time through the reschedule route and keeps the picker open when the time was taken', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(json(409, { type: 'slot_taken', status: 409 }));
    vi.stubGlobal('fetch', fetch);

    const result = await changeInterview('reschedule', form({ ...posted, change: '1' }), undefined);

    expect(result).toEqual({ outcome: 'slot-taken', tz: 'Asia/Kolkata', change: true });
    expect(String(fetch.mock.calls[0]![0])).toContain(`/interviews/${token}/reschedule`);
  });

  it('cancels with no body', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(json(204));
    vi.stubGlobal('fetch', fetch);

    expect((await changeInterview('cancel', form({ organization: 'acme', token }), undefined)).outcome).toBe('cancelled');
    const [url, init] = fetch.mock.calls[0]!;
    expect(String(url)).toContain(`/interviews/${token}/cancel`);
    expect((init as RequestInit).body).toBeUndefined();
  });

  it('asks for a time when none was posted, without calling the API', async () => {
    const fetch = vi.fn();
    vi.stubGlobal('fetch', fetch);

    expect((await changeInterview('book', form({ organization: 'acme', token }), undefined)).outcome).toBe('no-choice');
    expect((await changeInterview('book', form({ organization: 'acme', token, start: 'soon' }), undefined)).outcome).toBe('no-choice');
    expect(fetch).not.toHaveBeenCalled();
  });

  it('refuses a damaged link before it reaches an API path, and drops a zone it does not know', async () => {
    const fetch = vi.fn();
    vi.stubGlobal('fetch', fetch);

    expect(await changeInterview('cancel', form({ organization: 'acme', token: '../x', tz: 'Mars/Base' }), undefined)).toEqual({
      outcome: 'not-found',
      change: false,
    });
    expect(fetch).not.toHaveBeenCalled();
  });

  it('reports a closed link, a busy network and a server problem without throwing', async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(json(410, { type: 'cancelled', status: 410 }))
      .mockResolvedValueOnce(json(429))
      .mockRejectedValueOnce(new Error('connection refused'));
    vi.stubGlobal('fetch', fetch);

    expect((await changeInterview('book', form(posted), undefined)).outcome).toBe('closed');
    expect((await changeInterview('book', form(posted), undefined)).outcome).toBe('busy');
    expect((await changeInterview('book', form(posted), undefined)).outcome).toBe('error');
  });
});
