import { z } from 'zod';
import type { DayHours, DayOfWeek, Interviewer, InterviewOption, ScheduleInterviewRequest, StageScheduling, TeamDetail, WeeklyHours } from '@screenhire/api-client';
import {
  checkWindow,
  dayIn,
  defaultDayEnd,
  defaultDayStart,
  durations,
  isTimeOfDay,
  knowsZone,
  maxInterviewers,
  windowInstants,
  zonedMidnight,
  zonedWallClock,
  type WindowValues,
} from '#lib/schedule-interview.js';

// The three ways a candidate picks an interview time (docs/architecture/interview-scheduling.md, "Three ways to pick a
// time"), and the rules the Schedule interview and Change how they pick sheets share, kept out of the components so they
// can be tested alone.

/** Each option in the words the sheets use: the choice card's name and its one line. */
export const optionWords: Record<InterviewOption, { title: string; description: string; short: string }> = {
  OurTimes: { title: 'Pick from our times', description: 'They choose an open time, and it is booked at once.', short: 'Picks from our times' },
  TheirTimes: { title: "Tell us when you're free", description: 'They send 3 to 5 times that suit them, and you book one.', short: 'Tells us when they are free' },
  OurSuggestion: { title: "We'll suggest a time", description: 'You pick one or two times for them to accept.', short: 'Accepts a time we suggest' },
};

export const optionOrder: readonly InterviewOption[] = ['OurTimes', 'TheirTimes', 'OurSuggestion'];

/** The days of the week, Monday first, as the hours editors list them. */
export const weekDays: readonly { day: DayOfWeek; label: string }[] = [
  { day: 'Monday', label: 'Monday' },
  { day: 'Tuesday', label: 'Tuesday' },
  { day: 'Wednesday', label: 'Wednesday' },
  { day: 'Thursday', label: 'Thursday' },
  { day: 'Friday', label: 'Friday' },
  { day: 'Saturday', label: 'Saturday' },
  { day: 'Sunday', label: 'Sunday' },
];

/** Monday to Friday, `from` to `to`: the hours every interview had before hours could be set per day. */
export function weekdayHours(from: string = defaultDayStart, to: string = defaultDayEnd): DayHours[] {
  return weekDays.slice(0, 5).map(({ day }) => ({ day, from, to }));
}

/** The hours as the API takes them, Monday first. */
export const weekly = (days: readonly DayHours[]): WeeklyHours => ({ days: sortDays(days) });

export function sortDays(days: readonly DayHours[]): DayHours[] {
  const order = new Map(weekDays.map(({ day }, index) => [day, index]));
  return days.toSorted((a, b) => (order.get(a.day)! - order.get(b.day)!) || a.from.localeCompare(b.from));
}

const minutesOf = (time: string) => (time === '24:00' ? 24 * 60 : Number(time.slice(0, 2)) * 60 + Number(time.slice(3, 5)));

/** What is wrong with the hours, in words, or undefined. `minutes` must fit in at least one day. */
export function hoursProblem(days: readonly DayHours[], minutes?: number): string | undefined {
  if (days.length === 0) return 'Choose at least one day.';
  if (days.some((day) => minutesOf(day.to) <= minutesOf(day.from))) return 'Hours have to end after they start.';
  if (minutes !== undefined && days.every((day) => minutesOf(day.to) - minutesOf(day.from) < minutes)) return 'The hours are shorter than the interview.';
  return undefined;
}

/** The hours in words: "Mon to Fri, 9:00 AM to 5:00 PM" when every day is the same, else each day. */
export function hoursText(days: readonly DayHours[], timeLabel: (value: string) => string): string {
  if (days.length === 0) return 'No hours';
  const sorted = sortDays(days);
  const same = sorted.every((day) => day.from === sorted[0]!.from && day.to === sorted[0]!.to);
  const short = (day: DayOfWeek) => day.slice(0, 3);
  const range = (day: DayHours) => `${timeLabel(day.from)} to ${timeLabel(day.to)}`;
  if (!same) return sorted.map((day) => `${short(day.day)} ${range(day)}`).join(', ');
  const names = sorted.map((day) => day.day);
  const weekdays = weekDays.slice(0, 5).map(({ day }) => day);
  const label = names.length === 5 && names.every((day, index) => day === weekdays[index]) ? 'Mon to Fri' : names.map(short).join(', ');
  return `${label}, ${range(sorted[0]!)}`;
}

const localPattern = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})$/;

/**
 * The instant a wall-clock time (`YYYY-MM-DDTHH:mm`, a datetime-local input's value) is in `zone`, whatever zone the browser
 * is in, or null. A time the clocks skip reads as just after the gap.
 */
export function zonedTime(local: string, zone: string): number | null {
  const match = localPattern.exec(local);
  if (!match || !knowsZone(zone)) return null;
  const [year, month, day, hour, minute] = match.slice(1).map(Number) as [number, number, number, number, number];
  if (hour > 23 || minute > 59 || zonedMidnight(`${match[1]}-${match[2]}-${match[3]}`, zone) === null) return null;
  return zonedWallClock(Date.UTC(year, month - 1, day, hour, minute), zone);
}

/** The picked times that are filled in, as instants in `zone`; null when one of them is not a time. */
export function pickedInstants(values: readonly string[], zone: string): number[] | null {
  const filled = values.filter((value) => value.trim() !== '');
  const instants = filled.map((value) => zonedTime(value, zone));
  return instants.some((instant) => instant === null) ? null : (instants as number[]);
}

/** The values the Schedule interview sheet holds. */
export interface OptionValues extends WindowValues {
  option: InterviewOption;
  interviewerUserIds: string[];
  durationMinutes: number;
  timeZone: string;
  hours: DayHours[];
  useBlocks: boolean;
  /** We'll suggest a time: one or two `YYYY-MM-DDTHH:mm` times, blank when not used. */
  proposed: string[];
  /** Pick from our times: up to three one-off times. */
  extra: string[];
}

/** Whether a person can be ticked for this option: times from calendars need their time to be known; the others do not. */
export function canInterview(person: Interviewer, option: InterviewOption, useBlocks: boolean): boolean {
  return option !== 'OurTimes' || useBlocks || person.calendarConnected || person.hasHours === true;
}

/** What is wrong with the option's own fields, each on its field; the window and the zone are `checkWindow`'s. */
export function checkOption(value: OptionValues, issue: (field: keyof OptionValues, message: string) => void, now: number): void {
  if (!knowsZone(value.timeZone)) issue('timeZone', 'Choose a time zone.');
  if (value.option === 'OurSuggestion') {
    const times = pickedInstants(value.proposed, value.timeZone);
    if (times === null) issue('proposed', 'Write each time in full.');
    else if (times.length === 0) issue('proposed', 'Pick a time for them.');
    else if (times.some((time) => time <= now)) issue('proposed', 'Each time has to be in the future.');
    else if (new Set(times).size !== times.length) issue('proposed', 'Pick two different times.');
    return;
  }

  checkWindow(value, value.timeZone, (field, message) => issue(field, message), now);
  const problem = hoursProblem(value.hours, value.durationMinutes);
  if (problem) issue('hours', problem);
  if (value.option === 'OurTimes') {
    const extra = pickedInstants(value.extra, value.timeZone);
    const window = knowsZone(value.timeZone) ? windowInstants(value.windowStart, value.windowEnd, value.timeZone) : null;
    if (extra === null) issue('extra', 'Write each one-off time in full.');
    else if (extra.some((time) => time <= now)) issue('extra', 'A one-off time has to be in the future.');
    else if (window && extra.some((time) => time < Date.parse(window.windowStart) || time + value.durationMinutes * 60_000 > Date.parse(window.windowEnd)))
      issue('extra', 'A one-off time has to be inside the days they pick from.');
  }
}

/**
 * The days and times as the API takes them. We'll suggest a time has no days of its own: they run from today to the end of
 * the last time's day, so the invitation closes once its times have passed.
 */
export function optionPayload(value: OptionValues, now: number) {
  const iso = (instants: number[]) => instants.toSorted((a, b) => a - b).map((instant) => new Date(instant).toISOString());
  if (value.option === 'OurSuggestion') {
    const times = pickedInstants(value.proposed, value.timeZone) ?? [];
    const last = Math.max(...times);
    const window = windowInstants(dayIn(now, value.timeZone), dayIn(last, value.timeZone), value.timeZone);
    return window ? { option: value.option, ...window, proposedTimes: iso(times) } : null;
  }

  const window = windowInstants(value.windowStart, value.windowEnd, value.timeZone);
  if (!window) return null;
  return {
    option: value.option,
    ...window,
    hours: weekly(value.hours),
    useBlocks: value.option === 'OurTimes' && value.useBlocks,
    extraTimes: value.option === 'OurTimes' ? iso(pickedInstants(value.extra, value.timeZone) ?? []) : [],
  };
}

/** The whole request to offer the interview. */
export function scheduleRequest(value: OptionValues, now: number): ScheduleInterviewRequest | null {
  const option = optionPayload(value, now);
  return option ? { interviewerUserIds: value.interviewerUserIds, durationMinutes: value.durationMinutes, timeZone: value.timeZone, ...option } : null;
}

/** Who a stage's default names, resolved against the people who can interview for this job. */
export interface StageDefaultPeople {
  /** The default's people who see the job, in its order: they start ticked. */
  selected: string[];
  /** The default's people who do not see the job, by name: the sheet lists them, muted, and changes nobody's access. */
  notOnJob: string[];
  /** The stage names a team that was deleted since. */
  teamMissing: boolean;
}

export function stageDefaultPeople(
  scheduling: StageScheduling | null | undefined,
  teams: readonly TeamDetail[] | undefined,
  people: readonly Interviewer[],
): StageDefaultPeople {
  const none = { selected: [], notOnJob: [], teamMissing: false };
  if (!scheduling) return none;
  let named: { userId: string; displayName: string }[];
  if (scheduling.interviewerTeamId) {
    if (!teams) return none;
    const team = teams.find((candidate) => candidate.id === scheduling.interviewerTeamId);
    if (!team) return { ...none, teamMissing: true };
    named = team.members.map((member) => ({ userId: member.userId, displayName: member.displayName }));
  } else {
    named = (scheduling.interviewerUserIds ?? []).map((userId) => ({
      userId,
      displayName: people.find((person) => person.userId === userId)?.displayName ?? 'A former member',
    }));
  }

  const onJob = new Set(people.map((person) => person.userId));
  return {
    selected: named.filter((person) => onJob.has(person.userId)).map((person) => person.userId).slice(0, 5),
    notOnJob: named.filter((person) => !onJob.has(person.userId)).map((person) => person.displayName),
    teamMissing: false,
  };
}

/** The days a stage's default gives: from tomorrow, for its days ahead (14 when it names none). */
export function defaultWindow(now: number, zone: string, windowDays: number | null | undefined): WindowValues {
  const today = dayIn(now, knowsZone(zone) ? zone : 'UTC');
  const add = (days: number) => new Date(Date.parse(`${today}T00:00:00Z`) + days * 86_400_000).toISOString().slice(0, 10);
  return { windowStart: add(1), windowEnd: add(windowDays ?? 14) };
}

/** The Schedule interview form: the way to pick, the people and length, and what that way needs. */
export const scheduleSchema = z
  .object({
    option: z.enum(['OurTimes', 'TheirTimes', 'OurSuggestion']),
    interviewerUserIds: z
      .array(z.string())
      .min(1, 'Choose who will interview.')
      .max(maxInterviewers, `Choose 1 to ${maxInterviewers} interviewers.`),
    durationMinutes: z.number().refine((minutes) => (durations as readonly number[]).includes(minutes), 'Choose how long it lasts.'),
    windowStart: z.string(),
    windowEnd: z.string(),
    timeZone: z.string(),
    hours: z.array(
      z.object({
        day: z.enum(['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday']),
        from: z.string().refine(isTimeOfDay, 'Choose when the day starts.'),
        to: z.string().refine(isTimeOfDay, 'Choose when the day ends.'),
      }),
    ),
    useBlocks: z.boolean(),
    proposed: z.array(z.string()),
    extra: z.array(z.string()).max(3, 'Add at most 3 one-off times.'),
  })
  .superRefine((value, context) => {
    checkOption(value, (field, message) => context.addIssue({ code: 'custom', path: [field], message }), Date.now());
  });
