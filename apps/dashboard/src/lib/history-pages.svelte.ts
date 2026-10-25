import type { HistoryEntry, HistoryPage } from '@screenhire/api-client';
import { createQueries, type CreateQueryOptions, type QueryKey } from '@tanstack/svelte-query';
import { describeError } from '#lib/query.js';
import { joinPages } from '#lib/history.js';

/**
 * An object's change history as the History sheet reads it: the pages loaded so far, newest first, through TanStack
 * Query and the generated query options (`getGet...HistoryQueryOptions(id, { before })`), so a save that invalidates
 * the object's path refreshes them too. Nothing loads until `enabled` is true (the sheet is open); closing it goes back
 * to the first page.
 */
export function createHistoryPages<TResponse extends { data: HistoryPage }, TError>(
  page: (before: string | undefined) => CreateQueryOptions<TResponse, TError, TResponse, QueryKey>,
  enabled: () => boolean,
) {
  let cursors = $state<(string | undefined)[]>([undefined]);
  const queries = createQueries(() => ({
    queries: cursors.map((before) => ({ ...page(before), enabled: enabled() })),
  }));

  $effect(() => {
    if (!enabled()) cursors = [undefined];
  });

  // A change saved while older pages are loaded refetches them all, and the first page then starts at the new entry. The
  // second page's cursor still points at the old first page's end, so the entry pushed off the first page would fall
  // between the two: go back to the first page.
  $effect(() => {
    const next = queries[0]?.data?.data.nextBefore;
    if (next !== undefined && cursors.length > 1 && next !== cursors[1]) cursors = [undefined];
  });

  const last = () => queries[queries.length - 1];

  return {
    /** The loaded entries, or undefined until the first page arrives. */
    get entries(): HistoryEntry[] | undefined {
      return queries[0]?.data ? joinPages(queries.map((query) => query.data?.data)) : undefined;
    },
    get error(): string | undefined {
      const failed = queries.find((query) => query.isError);
      return failed ? describeError(failed.error, 'Could not load the history') : undefined;
    },
    get hasMore(): boolean {
      return last()?.data?.data.nextBefore != null;
    },
    get loadingMore(): boolean {
      return queries.length > 1 && Boolean(last()?.isPending);
    },
    retry() {
      for (const query of queries) if (query.isError) void query.refetch();
    },
    more() {
      const next = last()?.data?.data.nextBefore;
      if (next != null && !cursors.includes(next)) cursors = [...cursors, next];
    },
  };
}
