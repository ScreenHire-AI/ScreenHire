import type { ApplicationSummary } from '@screenhire/api-client';
import {
  createColumnHelper,
  createSortedRowModel,
  rowSortingFeature,
  sortFn_alphanumeric,
  sortFn_basic,
  tableFeatures,
  type ColumnDef,
} from '@tanstack/svelte-table';

/**
 * The candidate table's TanStack Table setup, shared with code that adds columns. It sorts (a column with an accessor
 * and sorting left on gets a sort button in its header) but does not filter: pages filter the rows before the table
 * sees them, by stage, saved view or verification level, so counts and "select all" match what is shown. Without a sort,
 * rows keep the API's order, newest first.
 */
export const candidateTableFeatures = tableFeatures({
  rowSortingFeature,
  sortedRowModel: createSortedRowModel(),
  sortFns: { basic: sortFn_basic, alphanumeric: sortFn_alphanumeric },
});

export type CandidateTableFeatures = typeof candidateTableFeatures;

// `any`: one list mixes columns of different value types, as TanStack's own `columns` helper does.
export type CandidateColumn = ColumnDef<CandidateTableFeatures, ApplicationSummary, any>;

/**
 * The columns a candidate table sorts by, for the sort it remembers (a stored sort on any other column falls back to
 * the default). The table and a page that owns its sort (a saved view sets it) both ask here.
 */
export function sortableColumnIds(extraColumns: readonly CandidateColumn[], showJob: boolean): string[] {
  return [
    'candidate',
    ...extraColumns.flatMap((column) => (column.id ? [column.id] : [])),
    ...(showJob ? ['job'] : []),
    'stage',
    'overdue',
    'source',
    'applied',
    'lastActivity',
  ];
}

/**
 * Builds extra columns for the table's `extraColumns` slot, which sits right after Candidate (status first, T2). The
 * verification columns go there; `verificationColumns` in #lib/verification-columns.ts is the worked example of a sortable accessor column. A
 * column that only draws something is a display column:
 *
 * ```ts
 * const tags = candidateColumns.display({ id: 'tags', header: 'Tags', cell: ({ row }) => ... });
 * ```
 */
export const candidateColumns = createColumnHelper<CandidateTableFeatures, ApplicationSummary>();

/** Each row is exactly this tall (design system section 5); the virtualizer relies on it. */
export const candidateRowHeight = 48;

/** A list's search: every word of the query appears in the candidate's name, or the job's title when `withJob`. */
export function matchesSearch(application: ApplicationSummary, query: string, withJob = false): boolean {
  const words = query.toLocaleLowerCase().split(/\s+/).filter(Boolean);
  if (words.length === 0) return true;
  const text = (withJob ? `${application.candidateName} ${application.jobTitle}` : application.candidateName).toLocaleLowerCase();
  return words.every((word) => text.includes(word));
}
