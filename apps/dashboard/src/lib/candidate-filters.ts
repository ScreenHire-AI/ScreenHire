import type { ApplicationSummary } from '@screenhire/api-client';
import type { FilterOption } from '#lib/components/filter-menu.svelte';
import { stageLabel } from '#lib/talent-pool.js';

/** Every job the applications are for, by title, each with how many applications it has. */
export function jobOptions(applications: readonly ApplicationSummary[]): FilterOption[] {
  const jobs = new Map<string, FilterOption>();
  for (const application of applications) {
    const job = jobs.get(application.jobId) ?? { value: application.jobId, label: application.jobTitle, count: 0 };
    job.count = (job.count ?? 0) + 1;
    jobs.set(application.jobId, job);
  }
  return [...jobs.values()].toSorted((a, b) => a.label.localeCompare(b.label));
}

// The order a pipeline runs in, so the Stage menu reads like one (custom stages go before the endings).
const kindOrder = ['Applied', 'Screening', 'Assessment', 'Verification', 'Interview', 'Custom', 'Offer', 'Hired', 'Rejected', 'Withdrawn', 'KeptInMind'];

/**
 * The stages of the picked jobs (every job's when none is picked), by name, so "Screening" in two jobs is one choice,
 * in pipeline order, each with how many of those applications are in it.
 */
export function stageOptions(applications: readonly ApplicationSummary[], jobIds: readonly string[]): FilterOption[] {
  const stages = new Map<string, FilterOption & { rank: number }>();
  for (const application of applications) {
    if (jobIds.length > 0 && !jobIds.includes(application.jobId)) continue;
    // A candidate kept in mind is under "Kept in mind", after the endings, never under the stage they left from.
    const name = stageLabel(application);
    const kind = application.keptInMindAt ? 'KeptInMind' : application.stage.kind;
    const stage = stages.get(name) ?? { value: name, label: name, count: 0, rank: kindOrder.indexOf(kind) };
    stage.count = (stage.count ?? 0) + 1;
    stages.set(name, stage);
  }
  return [...stages.values()].toSorted((a, b) => a.rank - b.rank || a.label.localeCompare(b.label)).map(({ rank: _rank, ...option }) => option);
}
