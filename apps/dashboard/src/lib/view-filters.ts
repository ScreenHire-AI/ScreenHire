import type { ApplicationSummary, ViewFilters, ViewScope } from '@screenhire/api-client';
import { stageOptions } from '#lib/candidate-filters.js';
import { findSavedView } from '#lib/saved-views.js';
import { matchesTag } from '#lib/tags.js';
import { stageLabel } from '#lib/talent-pool.js';
import {
  aiAssistanceLevels,
  authenticityLevels,
  matchesVerificationFilter,
  noVerificationFilter,
  type VerificationFilter,
} from '#lib/verification.js';

/**
 * What a saved view holds and how the candidate tables hand it over (docs/architecture/saved-views.md). A view is the
 * tables' filters and sort, never their search text. Stages are names on the Candidates page and stage ids on a job's
 * table. These are pure functions so the pages, the sidebar's counts and the tests agree on what a view means.
 */

/** The table's sorted column and direction. */
export interface SortChoice {
  id: string;
  desc: boolean;
}

/** The sort each table opens with when the person has not chosen one (design system L2). */
export const defaultSorts: Record<ViewScope, SortChoice> = {
  Candidates: { id: 'applied', desc: true },
  Job: { id: 'stage', desc: false },
};

/** What a table currently filters by, as its page holds it. */
export interface FilterParts {
  baseView?: string | null;
  jobIds?: readonly string[];
  stages?: readonly string[];
  verification?: VerificationFilter;
  tag?: string | null;
  sort: SortChoice;
}

/** The view filters for what the table shows now. */
export function filtersOf(parts: FilterParts): ViewFilters {
  const verification = parts.verification ?? noVerificationFilter;
  return {
    baseView: parts.baseView ?? null,
    jobIds: [...(parts.jobIds ?? [])],
    stages: [...(parts.stages ?? [])],
    authenticity: verification.authenticity,
    aiAssistance: verification.aiAssistance,
    tag: parts.tag ?? null,
    sortColumn: parts.sort.id,
    sortDescending: parts.sort.desc,
  };
}

/** The verification filter a view holds; a level this build does not know counts as no filter. */
export function verificationOf(filters: ViewFilters): VerificationFilter {
  const authenticity = filters.authenticity;
  const aiAssistance = filters.aiAssistance;
  return {
    authenticity:
      authenticity === 'Pending' || (authenticity && Object.hasOwn(authenticityLevels, authenticity))
        ? (authenticity as VerificationFilter['authenticity'])
        : null,
    aiAssistance: aiAssistance && Object.hasOwn(aiAssistanceLevels, aiAssistance) ? (aiAssistance as VerificationFilter['aiAssistance']) : null,
  };
}

/** The sort a view holds, or the table's own default when it holds none. */
export function sortOf(filters: ViewFilters, scope: ViewScope): SortChoice {
  return filters.sortColumn ? { id: filters.sortColumn, desc: filters.sortDescending } : defaultSorts[scope];
}

const sorted = (values: readonly string[]) => [...values].toSorted();

/** Whether two views pick the same rows in the same order; the order a list was picked in does not matter. */
export function sameFilters(a: ViewFilters, b: ViewFilters, scope: ViewScope): boolean {
  const sortA = sortOf(a, scope);
  const sortB = sortOf(b, scope);
  return (
    (a.baseView ?? null) === (b.baseView ?? null) &&
    sorted(a.jobIds).join() === sorted(b.jobIds).join() &&
    sorted(a.stages).join() === sorted(b.stages).join() &&
    (a.authenticity ?? null) === (b.authenticity ?? null) &&
    (a.aiAssistance ?? null) === (b.aiAssistance ?? null) &&
    (a.tag ?? null) === (b.tag ?? null) &&
    sortA.id === sortB.id &&
    sortA.desc === sortB.desc
  );
}

/** What a table shows with nothing picked: the built-in view it is on, if any, and its default sort. */
export function unfiltered(scope: ViewScope, baseView: string | null = null): ViewFilters {
  return filtersOf({ baseView, sort: defaultSorts[scope] });
}

/**
 * Whether a table's filters or sort differ from the view it is on (the Save view button). `current` is the view the
 * address names, or null for the table with nothing picked.
 */
export function differsFromView(filters: ViewFilters, scope: ViewScope, current: { filters: ViewFilters } | { baseView: string } | null): boolean {
  const baseline = current === null ? unfiltered(scope) : 'filters' in current ? current.filters : unfiltered(scope, current.baseView);
  return !sameFilters(filters, baseline, scope);
}

/**
 * The rows a saved view picks, for the sidebar's count. The same rules as the pages apply to the rows: the built-in view it
 * narrows, the verification levels, the tag, the jobs, and the stages that belong to the picked jobs. Search is not part
 * of a view.
 */
export function viewMatcher(
  filters: ViewFilters,
  scope: ViewScope,
  applications: readonly ApplicationSummary[],
  now: number,
): (application: ApplicationSummary) => boolean {
  const base = filters.baseView ? findSavedView(filters.baseView) : undefined;
  const verification = verificationOf(filters);
  // A stage picked before the jobs changed counts only while one of the picked jobs has it (the Candidates page's rule).
  const available = scope === 'Candidates' && filters.stages.length > 0 ? stageOptions(applications, filters.jobIds) : [];
  const names = scope === 'Candidates' ? filters.stages.filter((name) => available.some((stage) => stage.value === name)) : filters.stages;
  return (application) =>
    (!base || base.matches(application, now)) &&
    matchesVerificationFilter(application, verification) &&
    matchesTag(application, filters.tag ?? null) &&
    (filters.jobIds.length === 0 || filters.jobIds.includes(application.jobId)) &&
    (names.length === 0 ||
      (scope === 'Candidates' ? names.includes(stageLabel(application)) : names.includes(application.stage.id) && !application.keptInMindAt));
}
