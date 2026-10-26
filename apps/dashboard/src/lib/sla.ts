import { timeSince } from '#lib/dates.js';

/**
 * Stage time limits on screen (design system section 3.2). An application is overdue once its stage's time limit has
 * passed: the API sends when that is (`slaDueAt`), and the screens compare it with the dashboard's clock, so a row turns
 * overdue while the page is open. Overdue measures the hiring team's delay, never the candidate's.
 */
export type WithTimeLimit = { slaDueAt?: string | null };

/** Whether the application is past its stage's time limit at `now`. */
export const isOverdue = (application: WithTimeLimit, now: number): boolean =>
  Boolean(application.slaDueAt) && Date.parse(application.slaDueAt!) <= now;

/** The Overdue badge's label: `Overdue by 2 days`. */
export const overdueLabel = (dueAt: string, now: number): string => `Overdue by ${timeSince(dueAt, now)}`;
