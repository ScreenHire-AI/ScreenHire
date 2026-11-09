import type { ApplicationSummary } from '@screenhire/api-client';
import { isTerminal } from './workflow.js';

/** The Candidates page's three lists (docs/architecture/talent-pool.md): who is in play, who was saved, and everyone. */
export type CandidateList = 'current' | 'saved' | 'all';

/** The list an address asks for: `?list=saved` or `?list=all`, and Current otherwise. */
export function listOf(params: Pick<URLSearchParams, 'get'>): CandidateList {
  const list = params.get('list');
  return list === 'saved' || list === 'all' ? list : 'current';
}

/**
 * Whether an application is in a hiring process now: neither saved for future roles nor at an end stage (hired, rejected or
 * withdrawn). Current shows these; All shows every application, for finding someone whose process ended.
 */
export const isCurrentApplication = (application: Pick<ApplicationSummary, 'keptInMindAt' | 'stage'>): boolean =>
  !application.keptInMindAt && !isTerminal(application.stage.kind);

/** How many people each list holds; a list whose query has not answered yet has no count. */
export type ListCounts = Partial<Record<CandidateList, number>>;

/** The switch's counts from the application list and the saved people's count, as the two queries answer. */
export const listCounts = (applications: readonly ApplicationSummary[] | undefined, saved: number | undefined): ListCounts => ({
  current: applications?.filter(isCurrentApplication).length,
  saved,
  all: applications?.length,
});
