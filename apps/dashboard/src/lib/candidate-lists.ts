import type { ApplicationSummary, JobSummary } from '@screenhire/api-client';
import { isTerminal } from './workflow.js';

/** The Candidates page's three lists (docs/architecture/talent-pool.md): who is in play, who was saved, and everyone. */
export type CandidateList = 'current' | 'saved' | 'all';

/** The list an address asks for: `?list=saved` or `?list=all`, and Current otherwise. */
export function listOf(params: Pick<URLSearchParams, 'get'>): CandidateList {
  const list = params.get('list');
  return list === 'saved' || list === 'all' ? list : 'current';
}

/** The ids of the closed jobs in the job list (the shell's query), whose applications are no longer current. */
export const closedJobIds = (jobs: readonly Pick<JobSummary, 'id' | 'status'>[] | undefined): ReadonlySet<string> =>
  new Set((jobs ?? []).filter((job) => job.status === 'Closed').map((job) => job.id));

/**
 * Whether an application is in a hiring process now: on a job that is not closed, neither saved for future roles nor at
 * an end stage (hired, rejected or withdrawn). Current shows these; All shows every application, for finding someone
 * whose process ended.
 */
export const isCurrentApplication = (
  application: Pick<ApplicationSummary, 'keptInMindAt' | 'stage' | 'jobId'>,
  closedJobs: ReadonlySet<string> = new Set(),
): boolean => !application.keptInMindAt && !isTerminal(application.stage.kind) && !closedJobs.has(application.jobId);

/** How many people each list holds; a list whose query has not answered yet has no count. */
export type ListCounts = Partial<Record<CandidateList, number>>;

/** The switch's counts from the application list and the saved people's count, as the two queries answer. */
export const listCounts = (
  applications: readonly ApplicationSummary[] | undefined,
  saved: number | undefined,
  closedJobs: ReadonlySet<string> = new Set(),
): ListCounts => ({
  current: applications?.filter((application) => isCurrentApplication(application, closedJobs)).length,
  saved,
  all: applications?.length,
});
