import { renderComponent } from '@tanstack/svelte-table';
import { candidateColumns, type CandidateColumn } from '#lib/candidate-table.js';
import IntegrityChip from '#lib/components/integrity-chip.svelte';
import { chipOf, rankOf, type AssessmentKind } from '#lib/integrity.js';

function integrityColumn(kind: AssessmentKind, header: string): CandidateColumn {
  return candidateColumns.accessor((application) => rankOf(application, kind), {
    id: kind,
    header,
    // The first click puts the highest level first; pending rows stay at the end either way.
    sortFn: 'basic',
    sortDescFirst: true,
    sortUndefined: 'last',
    cell: ({ row }) => {
      const chip = chipOf(row.original.integrity, kind);
      return renderComponent(IntegrityChip, {
        kind,
        compact: true,
        level: chip?.level ?? null,
        reasons: chip?.reasons ?? 0,
        dismissed: chip?.dismissed ?? false,
      });
    },
  });
}

/** The candidate table's Authenticity and AI assistance chips, for its `extraColumns` slot; both sort by level. */
export const integrityColumns: readonly CandidateColumn[] = [integrityColumn('authenticity', 'Authenticity'), integrityColumn('aiAssistance', 'AI assistance')];
