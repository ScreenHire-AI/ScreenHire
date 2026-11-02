import type { ApplicationSummary, StageKind } from '@screenhire/api-client';
import type { FilterOption } from '#lib/components/filter-menu.svelte';
import { locales } from '#lib/dates.js';
import { stageLabel } from '#lib/talent-pool.js';
import { stageKindLabels } from '#lib/workflow.js';

/** The funnel's stage kinds, which a report can link to (docs/architecture/reports.md). */
const reachableKinds: readonly StageKind[] = ['Applied', 'Screening', 'Assessment', 'Interview', 'Offer', 'Hired'];

/** The applications a report row counts: those submitted between `from` and `to` that reached `kind` or a later stage. */
export interface ReachedFilter {
  kind: StageKind;
  from: string;
  to: string;
}

/** The funnel link's filter in the address (`?reached=Interview&from=...&to=...`), or null when it says none or nonsense. */
export function readReached(params: Pick<URLSearchParams, 'get'>): ReachedFilter | null {
  const kind = params.get('reached');
  const from = params.get('from');
  const to = params.get('to');
  if (!kind || !from || !to || !reachableKinds.includes(kind as StageKind)) return null;
  const start = Date.parse(from);
  const end = Date.parse(to);
  return Number.isFinite(start) && Number.isFinite(end) && start < end ? { kind: kind as StageKind, from, to } : null;
}

const days = new Intl.DateTimeFormat(locales, { dateStyle: 'medium' });

/** The filter in words, for its chip: "Reached Interview, Oct 1 to Oct 30, 2026". */
export function reachedLabel(filter: ReachedFilter): string {
  const last = new Date(Date.parse(filter.to) - 1);
  return `Reached ${stageKindLabels[filter.kind]}, ${days.formatRange(new Date(filter.from), last)}`;
}

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
    // A saved candidate is under "Saved", after the endings, never under the stage they left from.
    const name = stageLabel(application);
    const kind = application.keptInMindAt ? 'KeptInMind' : application.stage.kind;
    const stage = stages.get(name) ?? { value: name, label: name, count: 0, rank: kindOrder.indexOf(kind) };
    stage.count = (stage.count ?? 0) + 1;
    stages.set(name, stage);
  }
  return [...stages.values()].toSorted((a, b) => a.rank - b.rank || a.label.localeCompare(b.label)).map(({ rank: _rank, ...option }) => option);
}
