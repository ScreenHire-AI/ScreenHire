// A candidate's interview page on the careers server (docs/architecture/interview-scheduling.md): the candidate opens the
// signed link from the invitation email, picks a time, and can move or cancel it. Plain HTML forms posted to the
// `interviewBook`, `interviewReschedule` and `interviewCancel` actions, so the page ships no JavaScript and collects
// nothing but the choice (no telemetry, no device check). Every time is written on the server with Intl in the zone the
// candidate chose, so the page needs no script to convert it. Server-only where it calls the API.
import { bookPublicInterview, cancelPublicInterview, reschedulePublicInterview } from './api';
import { text } from './apply';
import { forwardedFor } from './apply-server';
import { isToken } from './questionnaire';

export { isToken };

/** What the page says after a post. `closed` and `not-found` say nothing: the page reads the interview again and shows what it is now. */
export type InterviewOutcome =
  | 'booked'
  | 'rescheduled'
  | 'cancelled'
  | 'slot-taken'
  | 'too-late'
  | 'already-booked'
  | 'not-booked'
  | 'no-choice'
  | 'unavailable'
  | 'busy'
  | 'error'
  | 'closed'
  | 'not-found';

export type InterviewAction = 'book' | 'reschedule' | 'cancel';

/** What an action hands the page: how it went, and the view to come back to (the zone and whether the time picker is open). */
export interface InterviewResult {
  outcome: InterviewOutcome;
  tz?: string;
  change?: boolean;
}

/** The outcomes the address may carry, so a crafted `?outcome=` cannot make the page say anything else. */
const SHOWN: readonly InterviewOutcome[] = [
  'booked',
  'rescheduled',
  'cancelled',
  'slot-taken',
  'too-late',
  'already-booked',
  'not-booked',
  'no-choice',
  'unavailable',
  'busy',
  'error',
];

/** The outcome an address carries, or null when it carries none the page knows. */
export const shownOutcome = (value: string | null | undefined): InterviewOutcome | null =>
  SHOWN.find((outcome) => outcome === value) ?? null;

/** Zones offered after the interview's own, spread across the world and short enough to scan. */
const COMMON_ZONES: readonly string[] = [
  'America/Los_Angeles',
  'America/Denver',
  'America/Chicago',
  'America/New_York',
  'America/Sao_Paulo',
  'Europe/London',
  'Europe/Berlin',
  'Africa/Johannesburg',
  'Asia/Dubai',
  'Asia/Kolkata',
  'Asia/Singapore',
  'Asia/Tokyo',
  'Australia/Sydney',
  'Pacific/Auckland',
  'UTC',
];

/** How many days with free times one screen shows. */
export const DAYS_PER_SCREEN = 5;

const LOCALE = 'en-US';

/** The zone when the runtime knows it, otherwise null. */
export function validZone(zone: string | null | undefined): string | null {
  if (!zone || zone.length > 64 || !/^[A-Za-z0-9_+\-/]+$/.test(zone)) return null;
  try {
    new Intl.DateTimeFormat(LOCALE, { timeZone: zone });
    return zone;
  } catch {
    return null;
  }
}

/** The zone to write times in: the API's answer, or UTC if the runtime does not know it. */
export const displayZone = (zone: string | null | undefined): string => validZone(zone) ?? 'UTC';

/** A start time posted from the form, as an ISO instant, or null for anything else. */
export function instantFrom(value: string | null | undefined): string | null {
  if (!value || value.length > 40 || !/^\d{4}-\d{2}-\d{2}T/.test(value)) return null;
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? null : parsed.toISOString();
}

function parts(instant: Date, zone: string, options: Intl.DateTimeFormatOptions): Record<string, string> {
  const found: Record<string, string> = {};
  for (const part of new Intl.DateTimeFormat(LOCALE, { timeZone: zone, ...options }).formatToParts(instant)) {
    found[part.type] = part.value;
  }

  return found;
}

/** The local calendar day of an instant in a zone, as `2026-03-08`. */
export function dayKey(instant: Date, zone: string): string {
  const day = parts(instant, zone, { year: 'numeric', month: '2-digit', day: '2-digit' });
  return `${day['year']}-${day['month']}-${day['day']}`;
}

// 24-hour with h23, because `hour12: false` writes midnight as 24:00; no AM/PM, so no narrow no-break space either.
const clock = (instant: Date, zone: string): string => {
  const time = parts(instant, zone, { hour: '2-digit', minute: '2-digit', hourCycle: 'h23' });
  return `${time['hour']}:${time['minute']}`;
};

const MINUTE = 60_000;

/** "10:00 to 10:30": the start and the end in the zone. Written out, because the range formatter uses an en dash. */
export const timeRange = (start: Date, minutes: number, zone: string): string =>
  `${clock(start, zone)} to ${clock(new Date(start.getTime() + minutes * MINUTE), zone)}`;

/** "Tuesday, March 3" (with the year when asked), the day an instant falls on in the zone. */
export function dayHeading(instant: Date, zone: string, withYear = false): string {
  const day = parts(instant, zone, { weekday: 'long', month: 'long', day: 'numeric', ...(withYear ? { year: 'numeric' } : {}) });
  return `${day['weekday']}, ${day['month']} ${day['day']}${withYear ? `, ${day['year']}` : ''}`;
}

/** The zone's name for people, such as "Eastern Time". Generic, so it holds whether or not daylight saving time is on. */
export function zoneName(instant: Date, zone: string): string {
  return parts(instant, zone, { timeZoneName: 'longGeneric' })['timeZoneName'] ?? zone;
}

/** One option of the zone select: the place and its offset from UTC at that time, "New York (GMT-5)". */
export function zoneLabel(zone: string, at: Date): string {
  if (zone === 'UTC') return 'UTC';
  const place = (zone.split('/').at(-1) ?? zone).replaceAll('_', ' ');
  return `${place} (${parts(at, zone, { timeZoneName: 'shortOffset' })['timeZoneName'] ?? 'GMT'})`;
}

export interface ZoneOption {
  value: string;
  label: string;
  selected: boolean;
}

/** The interview's own zone first, then the common ones, with the zone being shown added if it is none of them. */
export function zoneOptions(defaultZone: string, current: string, at: Date): ZoneOption[] {
  const zones: string[] = [];
  for (const zone of [defaultZone, ...COMMON_ZONES, current]) {
    const known = validZone(zone);
    if (known && !zones.includes(known)) zones.push(known);
  }

  return zones.map((value) => ({ value, label: zoneLabel(value, at), selected: value === current }));
}

export interface SlotChoice {
  /** The ISO instant that is posted. */
  value: string;
  /** "10:00 to 10:30". */
  label: string;
}

export interface SlotDay {
  /** The local day, `2026-03-08`. */
  key: string;
  heading: string;
  slots: SlotChoice[];
}

/** The offered times grouped by the local day they fall on in the zone, earliest first. A late slot can land on the next day. */
export function groupByDay(slots: readonly string[], zone: string, minutes: number): SlotDay[] {
  const instants = [...new Set(slots)]
    .map((value) => ({ value, at: new Date(value) }))
    .filter((slot) => !Number.isNaN(slot.at.getTime()))
    .sort((a, b) => a.at.getTime() - b.at.getTime());

  const days: SlotDay[] = [];
  const firstYear = instants[0] ? dayKey(instants[0].at, zone).slice(0, 4) : '';
  for (const { value, at } of instants) {
    const key = dayKey(at, zone);
    let day = days.at(-1);
    if (day?.key !== key) {
      // The year only where the days cross into another, so "January 2" is never two different years.
      day = { key, heading: dayHeading(at, zone, key.slice(0, 4) !== firstYear), slots: [] };
      days.push(day);
    }

    day.slots.push({ value, label: timeRange(at, minutes, zone) });
  }

  return days;
}

export interface SlotScreen {
  days: SlotDay[];
  /** Where the next screen starts, when there is one. */
  next: string | null;
  /** Whether this screen starts after the first day. */
  later: boolean;
}

/** A screen of days: those from `from` on (a local day, `2026-03-08`), at most DAYS_PER_SCREEN of them. */
export function screenOf(days: readonly SlotDay[], from: string | null | undefined): SlotScreen {
  const start = from && /^\d{4}-\d{2}-\d{2}$/.test(from) ? days.findIndex((day) => day.key >= from) : 0;
  const first = start < 0 ? 0 : start;
  const shown = days.slice(first, first + DAYS_PER_SCREEN);
  return { days: shown, next: days[first + DAYS_PER_SCREEN]?.key ?? null, later: first > 0 };
}

/** "30 minutes", "1 hour", "1 hour 30 minutes". */
export function durationText(minutes: number): string {
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  const plural = (count: number, unit: string) => `${count} ${unit}${count === 1 ? '' : 's'}`;
  return [hours > 0 ? plural(hours, 'hour') : '', rest > 0 || hours === 0 ? plural(rest, 'minute') : ''].filter(Boolean).join(' ');
}

/** "24 hours", "1 hour". */
export const hoursText = (hours: number): string => `${hours} hour${hours === 1 ? '' : 's'}`;

/** The booked time as the page writes it: the day with its year, the range and the zone's name. */
export function describeBooked(startsAt: string, minutes: number, zone: string): { day: string; range: string; zone: string } {
  const start = new Date(startsAt);
  return { day: dayHeading(start, zone, true), range: timeRange(start, minutes, zone), zone: zoneName(start, zone) };
}

/** The path of the interview page with the view to show: the zone, the picker, the screen of times, what just happened. */
export function interviewPath(
  base: string,
  token: string,
  view: { tz?: string | null; change?: boolean; from?: string | null; outcome?: InterviewOutcome | null } = {},
): string {
  const query = new URLSearchParams();
  if (view.tz) query.set('tz', view.tz);
  if (view.change) query.set('change', '1');
  if (view.from) query.set('from', view.from);
  if (view.outcome) query.set('outcome', view.outcome);
  const search = query.toString();
  return `${base}/interviews/${token}${search ? `?${search}` : ''}`;
}

export interface OutcomeNotice {
  /** `success` and `info` are calm; `problem` is something to act on. The page pairs each with an icon and these words. */
  kind: 'success' | 'info' | 'problem';
  message: string;
}

/** The words for an outcome, or null when the page itself says it all. */
export function noticeFor(outcome: InterviewOutcome | null): OutcomeNotice | null {
  switch (outcome) {
    case 'booked':
      return { kind: 'success', message: 'We are sending a confirmation to your email.' };
    case 'rescheduled':
      return { kind: 'success', message: 'Moved to the new time. We are sending a confirmation to your email.' };
    case 'slot-taken':
      return { kind: 'problem', message: 'That time was just taken. Choose another.' };
    case 'too-late':
      return { kind: 'problem', message: 'The interview is too close to change here. Reply to the email to reach the hiring team.' };
    case 'already-booked':
      return { kind: 'info', message: 'A time is already booked. Use Pick another time to move it.' };
    case 'not-booked':
      return { kind: 'problem', message: 'No time is booked yet. Choose one below.' };
    case 'no-choice':
      return { kind: 'problem', message: 'Choose a time first.' };
    case 'unavailable':
      return { kind: 'problem', message: 'We could not check the calendar just now. Try again in a few minutes.' };
    case 'busy':
      return { kind: 'problem', message: 'Too many tries from your network. Wait a minute, then try again.' };
    case 'error':
      return { kind: 'problem', message: 'Something went wrong on our side. Try again in a moment.' };
    default:
      return null;
  }
}

/** The code of a problem: the API sets the problem's `type` to it, and a full address ending in it counts too. */
const problemCode = (problem: unknown): string => {
  const type = (problem as { type?: string | null } | null | undefined)?.type;
  return typeof type === 'string' ? (type.split('/').at(-1) ?? '') : '';
};

/** What an API answer to a booking, a move or a cancellation means for the page. */
export function outcomeOf(action: InterviewAction, status: number, problem?: unknown): InterviewOutcome {
  switch (status) {
    case 204:
      return action === 'book' ? 'booked' : action === 'reschedule' ? 'rescheduled' : 'cancelled';
    case 404:
      return 'not-found';
    case 410:
      return 'closed';
    case 429:
      return 'busy';
    case 503:
      return 'unavailable';
    case 409:
      switch (problemCode(problem)) {
        case 'slot_taken':
          return 'slot-taken';
        case 'too_late':
          return 'too-late';
        case 'already_booked':
          return 'already-booked';
        case 'not_booked':
          return 'not-booked';
        default:
          return 'error';
      }
    default:
      return 'error';
  }
}

/**
 * Handles one posted form: books, moves or cancels. The candidate's mistakes (no time picked, a link that no longer
 * works) come back as an outcome; the page redirects to its own address with it, so a refresh never posts twice. Never throws.
 */
export async function changeInterview(action: InterviewAction, form: FormData, clientAddress: string | undefined): Promise<InterviewResult> {
  const organization = text(form, 'organization');
  const token = text(form, 'token');
  const view = { tz: validZone(text(form, 'tz')) ?? undefined, change: text(form, 'change') === '1' };
  if (!isToken(token)) return { outcome: 'not-found', ...view };

  const forwarded = forwardedFor(clientAddress);
  try {
    if (action === 'cancel') {
      const response = await cancelPublicInterview(organization, token, forwarded);
      return { outcome: outcomeOf(action, response.status, response.status === 409 || response.status === 410 ? response.data : undefined), ...view };
    }

    const start = instantFrom(text(form, 'start'));
    if (!start) return { outcome: 'no-choice', ...view };
    const send = action === 'book' ? bookPublicInterview : reschedulePublicInterview;
    const response = await send(organization, token, { start }, forwarded);
    return { outcome: outcomeOf(action, response.status, response.status === 409 || response.status === 410 ? response.data : undefined), ...view };
  } catch {
    return { outcome: 'error', ...view };
  }
}
