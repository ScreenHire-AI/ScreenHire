import { fireEvent, render, screen, within } from '@testing-library/svelte';
import { beforeEach, describe, expect, it } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import SortTableHarness from '../../test-support/sort-table-harness.svelte';

const names = () =>
  screen
    .getAllByRole('row')
    .slice(1)
    .map((row) => within(row).getAllByRole('cell')[0]!.textContent);
const header = (name: string) => screen.getByRole('columnheader', { name: new RegExp(name) });

// The sort is remembered in localStorage, which outlasts each test.
beforeEach(() => {
  localStorage.clear();
});

describe('sort header', () => {
  it('opens on the default sort of the table, marked on its header', async () => {
    render(SortTableHarness);

    expect(names()).toEqual(['Brightwater engineering', 'Software engineer', 'Hourly role']);
    expect(header('Last updated')).toHaveAttribute('aria-sort', 'descending');
    expect(header('Name')).not.toHaveAttribute('aria-sort');
    await expectNoAxeViolations(document.body);
  });

  it('sorts by a column on the first click and reverses it on the second, never going back to unsorted', async () => {
    render(SortTableHarness);

    await fireEvent.click(screen.getByRole('button', { name: 'Name' }));
    expect(names()).toEqual(['Brightwater engineering', 'Hourly role', 'Software engineer']);
    expect(header('Name')).toHaveAttribute('aria-sort', 'ascending');
    expect(header('Last updated')).not.toHaveAttribute('aria-sort');

    await fireEvent.click(screen.getByRole('button', { name: 'Name' }));
    expect(names()).toEqual(['Software engineer', 'Hourly role', 'Brightwater engineering']);
    expect(header('Name')).toHaveAttribute('aria-sort', 'descending');

    await fireEvent.click(screen.getByRole('button', { name: 'Name' }));
    expect(header('Name')).toHaveAttribute('aria-sort', 'ascending');
  });

  it('stores the sort a person chooses and opens on it after a reload', async () => {
    const first = render(SortTableHarness, { props: { sortId: 'templates' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Name' }));
    expect(JSON.parse(localStorage.getItem('screenhire:sort:templates')!)).toEqual({ id: 'name', desc: false });
    first.unmount();

    render(SortTableHarness, { props: { sortId: 'templates' } });
    expect(header('Name')).toHaveAttribute('aria-sort', 'ascending');
    expect(names()).toEqual(['Brightwater engineering', 'Hourly role', 'Software engineer']);
  });

  it('opens on a sort stored before the page loaded', () => {
    localStorage.setItem('screenhire:sort:templates', JSON.stringify({ id: 'updatedAt', desc: false }));
    render(SortTableHarness, { props: { sortId: 'templates' } });

    expect(header('Last updated')).toHaveAttribute('aria-sort', 'ascending');
    expect(names()).toEqual(['Hourly role', 'Software engineer', 'Brightwater engineering']);
  });

  it('falls back to the default when the stored sort names a column the table no longer has, or cannot be read', () => {
    localStorage.setItem('screenhire:sort:templates', JSON.stringify({ id: 'version', desc: true }));
    const first = render(SortTableHarness, { props: { sortId: 'templates' } });
    expect(header('Last updated')).toHaveAttribute('aria-sort', 'descending');
    first.unmount();

    localStorage.setItem('screenhire:sort:templates', 'not json');
    render(SortTableHarness, { props: { sortId: 'templates' } });
    expect(header('Last updated')).toHaveAttribute('aria-sort', 'descending');
  });

  it('forgets the sort of a table with no name', async () => {
    render(SortTableHarness);
    await fireEvent.click(screen.getByRole('button', { name: 'Name' }));

    expect(localStorage.length).toBe(0);
  });

  it('offers the same choices as the headers in the Sort chip, where choosing the sorted column reverses it', async () => {
    render(SortTableHarness);

    await fireEvent.click(screen.getByRole('button', { name: 'Sort' }));
    expect((await screen.findAllByRole('menuitemradio')).map((item) => item.textContent?.trim())).toEqual(['Name', 'Last updated, descending']);
    await fireEvent.click(screen.getByRole('menuitemradio', { name: 'Name' }));
    expect(header('Name')).toHaveAttribute('aria-sort', 'ascending');

    await fireEvent.click(screen.getByRole('button', { name: 'Sort' }));
    await fireEvent.click(await screen.findByRole('menuitemradio', { name: /Name/ }));
    expect(header('Name')).toHaveAttribute('aria-sort', 'descending');
  });
});
