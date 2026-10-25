import { functionalUpdate, type SortingState, type Updater } from '@tanstack/svelte-table';
import { preferences, type TableSort } from '#lib/preferences.svelte.js';

/**
 * A TanStack table's sort (design system L2): one column at a time, opening on the table's default, and remembered per
 * table and per browser through `preferences`. Pass it to `createTable` with `tableSortOptions`, so a second click on a
 * header reverses the sort instead of clearing it.
 *
 * ```ts
 * const sort = createTableSort('templates', { id: 'updatedAt', desc: true }, ['name', 'updatedAt']);
 * const table = createTable({ ...tableSortOptions(() => sort), features, columns, data });
 * ```
 */
export class TableSortState {
  sorting = $state<SortingState>([]);

  readonly #table: string | undefined;

  constructor(table: string | undefined, fallback: TableSort | undefined, columnIds?: readonly string[]) {
    this.#table = table;
    // A stored sort on a column the table no longer has falls back to the default.
    const stored = table ? preferences.sortFor(table) : undefined;
    const initial = stored && (!columnIds || columnIds.includes(stored.id)) ? stored : fallback;
    this.sorting = initial ? [{ id: initial.id, desc: initial.desc }] : [];
  }

  /** TanStack's `onSortingChange`: keeps the first sorted column and remembers it. */
  readonly onSortingChange = (updater: Updater<SortingState>): void => {
    const next = functionalUpdate(updater, this.sorting).slice(0, 1);
    this.sorting = next;
    const first = next[0];
    if (first && this.#table) {
      preferences.setSort(this.#table, { id: first.id, desc: first.desc });
    }
  };
}

/** `tableId` names the table in storage (`candidates`); leave it out for a sort that is not remembered. */
export function createTableSort(tableId: string | undefined, fallback?: TableSort, columnIds?: readonly string[]): TableSortState {
  return new TableSortState(tableId, fallback, columnIds);
}

/** The `createTable` options for a remembered, two-state, single-column sort. A getter, so a sort passed as a prop can change. */
export function tableSortOptions(sort: () => TableSortState) {
  return {
    state: {
      get sorting() {
        return sort().sorting;
      },
    },
    onSortingChange: (updater: Updater<SortingState>) => sort().onSortingChange(updater),
    enableSortingRemoval: false,
    enableMultiSort: false,
  };
}
