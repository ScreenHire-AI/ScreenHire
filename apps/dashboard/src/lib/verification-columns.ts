import { renderComponent } from '@tanstack/svelte-table';
import { candidateColumns, type CandidateColumn } from '#lib/candidate-table.js';
import VerificationCell from '#lib/components/verification-cell.svelte';
import { verificationRank } from '#lib/verification.js';

/**
 * The candidate table's one Verification column, for its `extraColumns` slot (design system section 21): the state as one
 * word, with the AI mark beside it for Some and Strong signs. It sorts Verify, Review, Dismissed, Clear, then Pending; the
 * first click puts the highest first and pending rows stay at the end either way. AI help never sorts.
 */
export const verificationColumns: readonly CandidateColumn[] = [
  candidateColumns.accessor((application) => verificationRank(application), {
    id: 'verification',
    header: 'Verification',
    sortFn: 'basic',
    sortDescFirst: true,
    sortUndefined: 'last',
    cell: ({ row }) => renderComponent(VerificationCell, { summary: row.original.verification }),
  }),
];
