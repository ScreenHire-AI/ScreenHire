import type { CalendarProblem } from '@screenhire/api-client';

/** What is wrong, and what fixes it, when a connected calendar stopped working (E2). */
export const calendarProblems: Record<NonNullable<CalendarProblem>, string> = {
  NeedsReconnect: 'Your calendar no longer accepts ScreenHire. Connect it again to keep scheduling interviews.',
};

/** How an OAuth round trip to a calendar ended, from the callback's `?calendar=` value. */
export type CalendarOutcome = 'connected' | 'denied' | 'expired' | 'failed';

const failures: Record<Exclude<CalendarOutcome, 'connected'>, string> = {
  denied: 'Your calendar was not connected: it was cancelled on the calendar.',
  expired: 'That took too long. Try again.',
  failed: 'Your calendar could not be connected. Try again.',
};

/** The words for a failed round trip, or undefined for a success or no outcome at all. */
export function calendarFailure(outcome: string | null): string | undefined {
  return outcome !== null && Object.hasOwn(failures, outcome) ? failures[outcome as keyof typeof failures] : undefined;
}

/** Whether `?calendar=` holds a round trip's outcome, success or not. */
export const isCalendarOutcome = (value: string | null): value is CalendarOutcome => value === 'connected' || calendarFailure(value) !== undefined;
