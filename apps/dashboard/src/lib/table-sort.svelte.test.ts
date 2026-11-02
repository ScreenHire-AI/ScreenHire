import { describe, expect, it } from 'vitest';
import { createTableSort } from './table-sort.svelte.js';

describe('showing a sort on a table', () => {
  const sort = () => createTableSort(undefined, { id: 'applied', desc: true }, ['applied', 'candidate']);

  it('sorts by a column the table has', () => {
    const table = sort();

    table.show({ id: 'candidate', desc: false });

    expect(table.sorting).toEqual([{ id: 'candidate', desc: false }]);
  });

  it('falls back to the default for a column the table does not have, as a saved view from an older build may name', () => {
    const table = sort();

    table.show({ id: 'gone', desc: false });

    expect(table.sorting).toEqual([{ id: 'applied', desc: true }]);
  });

  it('falls back to the default when there is nothing to show', () => {
    const table = sort();
    table.show({ id: 'candidate', desc: false });

    table.show(undefined);

    expect(table.sorting).toEqual([{ id: 'applied', desc: true }]);
  });
});
