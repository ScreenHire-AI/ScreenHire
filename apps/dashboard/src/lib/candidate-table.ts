import type { ApplicationSummary } from '@screenhire/api-client';
import { createColumnHelper, tableFeatures, type ColumnDef } from '@tanstack/svelte-table';

/**
 * The candidate table's TanStack Table setup, shared with code that adds columns. The table is core-only: no sorting
 * or filtering features, because the page filters by stage and the API orders newest first.
 */
export const candidateTableFeatures = tableFeatures({});

export type CandidateTableFeatures = typeof candidateTableFeatures;

// `any`: one list mixes columns of different value types, as TanStack's own `columns` helper does.
export type CandidateColumn = ColumnDef<CandidateTableFeatures, ApplicationSummary, any>;

/**
 * Builds extra columns for the table's `extraColumns` slot, which sits between Stage and Source. The integrity columns
 * (authenticity level, AI assistance) go there:
 *
 * ```ts
 * const integrity = candidateColumns.display({ id: 'authenticity', header: 'Authenticity', cell: ({ row }) => ... });
 * ```
 */
export const candidateColumns = createColumnHelper<CandidateTableFeatures, ApplicationSummary>();

/** Each row is exactly this tall (design-system section 4); the virtualizer relies on it. */
export const candidateRowHeight = 40;
