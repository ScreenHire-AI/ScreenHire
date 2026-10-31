import { renderComponent } from '@tanstack/svelte-table';
import { candidateColumns, type CandidateColumn } from '#lib/candidate-table.js';
import VerificationChip from '#lib/components/verification-chip.svelte';
import { chipOf, rankOf, type AssessmentKind } from '#lib/verification.js';

function verificationColumn(kind: AssessmentKind, header: string): CandidateColumn {
  return candidateColumns.accessor((application) => rankOf(application, kind), {
    id: kind,
    header,
    // The first click puts the highest level first; pending rows stay at the end either way.
    sortFn: 'basic',
    sortDescFirst: true,
    sortUndefined: 'last',
    cell: ({ row }) => {
      const chip = chipOf(row.original.verification, kind);
      return renderComponent(VerificationChip, {
        kind,
        compact: true,
        level: chip?.level ?? null,
        reasons: chip?.reasons ?? 0,
        dismissed: chip?.dismissed ?? false,
        // AI assistance is information, so its lowest level is the default and a calm row says nothing (section 21).
        // Authenticity keeps its quiet "No concerns": the column's one status (T3).
        hideCalm: kind === 'aiAssistance',
      });
    },
  });
}

/** The candidate table's Authenticity and AI assistance chips, for its `extraColumns` slot; both sort by level. */
export const verificationColumns: readonly CandidateColumn[] = [verificationColumn('authenticity', 'Authenticity'), verificationColumn('aiAssistance', 'AI assistance')];
