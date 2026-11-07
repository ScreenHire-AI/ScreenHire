import { z } from 'zod';
import { locales } from '#lib/dates.js';
import { zoneName } from '#lib/report-range.js';

// The Schedule interview sheet's rules (docs/architecture/interview-scheduling.md), kept out of the component so they can
// be tested alone. People pick days and working hours in a time zone they choose (design system W8); the API takes
// instants and an IANA zone id, so the conversion lives here.

/** The interview lengths the API accepts, in minutes. */
export const durations = [15, 30, 45, 60, 90, 120] as const;

/** At most this many people on one interview; the first is the organizer. */
export const maxInterviewers = 5;

/** The longest window the API takes, in days. */
export const maxWindowDays = 90;

const dayMs = 86_400_000;

const dayPattern = /^(\d{4})-(\d{2})-(\d{2})$/;

/** The parts of a `YYYY-MM-DD` day, or null when it is not a real date. */
function parseDay(text: string): { year: number; month: number; day: number } | null {
  const match = dayPattern.exec(text);
  if (!match) return null;
  const [year, month, day] = [Number(match[1]), Number(match[2]), Number(match[3])];
  const at = new Date(Date.UTC(year, month - 1, day));
  return at.getUTCFullYear() === year && at.getUTCMonth() === month - 1 && at.getUTCDate() === day ? { year, month, day } : null;
}

const wallClocks = new Map<string, Intl.DateTimeFormat>();

// The zone's wall clock reader. `hourCycle: 'h23'` because some engines write midnight as 24 otherwise.
function wallClock(zone: string): Intl.DateTimeFormat {
  let format = wallClocks.get(zone);
  if (!format) {
    format = new Intl.DateTimeFormat('en-US', {
      timeZone: zone,
      hourCycle: 'h23',
      year: 'numeric',
      month: 'numeric',
      day: 'numeric',
      hour: 'numeric',
      minute: 'numeric',
      second: 'numeric',
    });
    wallClocks.set(zone, format);
  }
  return format;
}

/** Whether the browser knows `zone` by that IANA id. */
export function knowsZone(zone: string): boolean {
  if (!zone) return false;
  try {
    wallClock(zone);
    return true;
  } catch {
    return false;
  }
}

// How far the zone's clock is ahead of UTC at an instant, in milliseconds.
function offsetAt(instant: number, zone: string): number {
  const parts: Record<string, number> = {};
  for (const part of wallClock(zone).formatToParts(new Date(instant))) {
    if (part.type !== 'literal') parts[part.type] = Number(part.value);
  }
  const asUtc = Date.UTC(parts.year!, parts.month! - 1, parts.day!, parts.hour! % 24, parts.minute!, parts.second!);
  return asUtc - Math.floor(instant / 1000) * 1000;
}

/**
 * The instant a day starts in `zone`, whatever zone the browser is in. The offset is read at a first guess and again at
 * the corrected one, because the guess can fall on the other side of a clock change.
 */
export function zonedMidnight(day: string, zone: string): number | null {
  const parts = parseDay(day);
  if (!parts || !knowsZone(zone)) return null;
  const wall = Date.UTC(parts.year, parts.month - 1, parts.day);
  const guess = wall - offsetAt(wall, zone);
  return wall - offsetAt(guess, zone);
}

/** `days` after (or, negative, before) a `YYYY-MM-DD` day. */
export function addDays(day: string, days: number): string {
  const parts = parseDay(day);
  if (!parts) return day;
  return new Date(Date.UTC(parts.year, parts.month - 1, parts.day + days)).toISOString().slice(0, 10);
}

/** The calendar day an instant falls on in `zone`, as `YYYY-MM-DD`. */
export function dayIn(instant: number, zone: string): string {
  const parts: Record<string, string> = {};
  for (const part of wallClock(zone).formatToParts(new Date(instant))) parts[part.type] = part.value;
  return `${parts.year!.padStart(4, '0')}-${parts.month!.padStart(2, '0')}-${parts.day!.padStart(2, '0')}`;
}

/** The days the window opens and closes on when nobody has chosen: tomorrow to two weeks from today, in `zone`. */
export function defaultDays(now: number, zone: string): { windowStart: string; windowEnd: string } {
  const today = dayIn(now, knowsZone(zone) ? zone : 'UTC');
  return { windowStart: addDays(today, 1), windowEnd: addDays(today, 14) };
}

/** How many days a window covers, both ends included. */
export function daysCovered(first: string, last: string): number | null {
  const start = parseDay(first);
  const end = parseDay(last);
  if (!start || !end) return null;
  return Math.round((Date.UTC(end.year, end.month - 1, end.day) - Date.UTC(start.year, start.month - 1, start.day)) / dayMs) + 1;
}

/**
 * The window as the API takes it: the first day's start to the end of the last day (the start of the day after), in
 * `zone`. Null when a day or the zone is not real. A clock change inside a 90-day window can make it an hour over the
 * API's 90 days of elapsed time, so the end is held to that; the hour lost is the last night, outside any working day.
 */
export function windowInstants(first: string, last: string, zone: string): { windowStart: string; windowEnd: string } | null {
  const start = zonedMidnight(first, zone);
  const end = zonedMidnight(addDays(last, 1), zone);
  if (start === null || end === null || parseDay(last) === null) return null;
  const capped = Math.min(end, start + maxWindowDays * dayMs);
  return { windowStart: new Date(start).toISOString(), windowEnd: new Date(capped).toISOString() };
}

/** The first and last day of a window, as the Schedule interview form and the Offer new times form both take them. */
export interface WindowValues {
  windowStart: string;
  windowEnd: string;
}

/** What is wrong with a window's days in `zone`, each on its field. The API names the same fields. */
export function checkWindow(value: WindowValues, zone: string, issue: (field: keyof WindowValues, message: string) => void, now: number): void {
  const first = parseDay(value.windowStart);
  const last = parseDay(value.windowEnd);
  if (!first) issue('windowStart', 'Pick the first day.');
  if (!last) issue('windowEnd', 'Pick the last day.');
  if (first && last) {
    const covered = daysCovered(value.windowStart, value.windowEnd)!;
    if (covered < 1) issue('windowEnd', 'The last day has to be the first day or later.');
    else if (covered > maxWindowDays) issue('windowEnd', `The window can be at most ${maxWindowDays} days.`);
    else {
      const instants = knowsZone(zone) ? windowInstants(value.windowStart, value.windowEnd, zone) : null;
      if (instants && Date.parse(instants.windowEnd) <= now) issue('windowEnd', 'The last day has to be today or later.');
    }
  }
}

const minutesOf = (time: string) => Number(time.slice(0, 2)) * 60 + Number(time.slice(3, 5));

export interface ScheduleValues {
  interviewerUserIds: string[];
  durationMinutes: number;
  windowStart: string;
  windowEnd: string;
  timeZone: string;
  dayStart: string;
  dayEnd: string;
}

/**
 * What is wrong with the choices, in words, each on the field it belongs to. The API checks the same things and names the
 * same fields (`windowEnd` for any problem with the window), so a refusal lands in the same place.
 */
export function checkSchedule(value: ScheduleValues, issue: (field: keyof ScheduleValues, message: string) => void, now: number): void {
  if (!knowsZone(value.timeZone)) issue('timeZone', 'Choose a time zone.');
  checkWindow(value, value.timeZone, issue, now);
  if (minutesOf(value.dayEnd) <= minutesOf(value.dayStart)) issue('dayEnd', 'Working hours have to end after they start.');
  else if (minutesOf(value.dayEnd) - minutesOf(value.dayStart) < value.durationMinutes) issue('dayEnd', 'Working hours are shorter than the interview.');
}

const timePattern = /^([01]\d|2[0-3]):[0-5]\d$/;

export const scheduleSchema = z
  .object({
    interviewerUserIds: z
      .array(z.string())
      .min(1, 'Choose who will interview.')
      .max(maxInterviewers, `Choose 1 to ${maxInterviewers} interviewers.`),
    durationMinutes: z.number().refine((minutes) => (durations as readonly number[]).includes(minutes), 'Choose how long it lasts.'),
    windowStart: z.string(),
    windowEnd: z.string(),
    timeZone: z.string(),
    dayStart: z.string().regex(timePattern, 'Choose when the day starts.'),
    dayEnd: z.string().regex(timePattern, 'Choose when the day ends.'),
  })
  .superRefine((value, context) => {
    checkSchedule(value, (field, message) => context.addIssue({ code: 'custom', path: [field], message }), Date.now());
  });

/** The Offer new times form: the days the candidate may pick from, in the interview's own zone. */
export const newTimesSchema = (zone: string) =>
  z.object({ windowStart: z.string(), windowEnd: z.string() }).superRefine((value, context) => {
    checkWindow(value, zone, (field, message) => context.addIssue({ code: 'custom', path: [field], message }), Date.now());
  });

/**
 * The people ticked, in the order they were ticked: the first is the organizer, whose calendar sends the invitation.
 * Unticking removes one, and ticking them again puts them last.
 */
export function toggleInterviewer(selected: readonly string[], userId: string, on: boolean): string[] {
  const without = selected.filter((id) => id !== userId);
  return on ? [...without, userId] : without;
}

/** The half-hour times of a day, as `HH:mm` values with the browser's own way of writing the time. */
export const timeOptions: { value: string; label: string }[] = (() => {
  const format = new Intl.DateTimeFormat(locales, { timeStyle: 'short', timeZone: 'UTC' });
  return Array.from({ length: 48 }, (_, index) => {
    const value = `${String(Math.floor(index / 2)).padStart(2, '0')}:${index % 2 === 0 ? '00' : '30'}`;
    return { value, label: format.format(new Date(Date.UTC(2026, 0, 1, Math.floor(index / 2), (index % 2) * 30))) };
  });
})();

export const defaultDayStart = '09:00';
export const defaultDayEnd = '17:00';

/** The interview lengths as the select words them. */
export const durationLabel = (minutes: number): string =>
  minutes === 60 ? '1 hour' : minutes === 90 ? '1 hour 30 minutes' : minutes === 120 ? '2 hours' : `${minutes} minutes`;

let zones: { value: string; label: string }[] | undefined;
let zonesFor = '';

/**
 * Every zone the browser knows, by its place and its common name ("New York (Eastern Time)", W8), sorted by place, with
 * the IANA id as the value. `current` stays in the list even if the browser's own list leaves it out.
 */
export function timeZoneOptions(current: string = zoneName()): { value: string; label: string }[] {
  if (zones && zonesFor === current) return zones;
  const known: string[] = typeof Intl.supportedValuesOf === 'function' ? Intl.supportedValuesOf('timeZone') : [];
  const ids = new Set([...known, 'UTC', ...(knowsZone(current) ? [current] : [])]);
  zones = [...ids]
    .map((value) => {
      let name = '';
      try {
        name = new Intl.DateTimeFormat(locales, { timeZone: value, timeZoneName: 'longGeneric' }).formatToParts(new Date(0)).find((part) => part.type === 'timeZoneName')?.value ?? '';
      } catch {
        // The id stays as its own label.
      }
      const place = value === 'UTC' ? 'UTC' : value.split('/').pop()!.replaceAll('_', ' ');
      return { value, label: name && name !== place ? `${place} (${name})` : place };
    })
    .toSorted((a, b) => a.label.localeCompare(b.label, locales));
  zonesFor = current;
  return zones;
}
