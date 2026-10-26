import { renderComponent } from '@tanstack/svelte-table';
import { candidateColumns, type CandidateColumn } from '#lib/candidate-table.js';
import Rating from '#lib/components/rating.svelte';

/**
 * A job's Rating column (design system section 11, Rating), for the candidate table's `extraColumns` slot: the stars
 * and the number from people's scorecards. It sorts by the rating, highest first; "Not rated yet" and "Rating after your
 * scorecard" have no number, so they stay at the end either way. Integrity output never feeds it (ADR-0013).
 */
export const ratingColumn: CandidateColumn = candidateColumns.accessor(
  (application) => (application.rating?.state === 'Rated' ? (application.rating.value ?? undefined) : undefined),
  {
    id: 'rating',
    header: 'Rating',
    sortFn: 'basic',
    sortDescFirst: true,
    sortUndefined: 'last',
    cell: ({ row }) => renderComponent(Rating, { rating: row.original.rating }),
  },
);
