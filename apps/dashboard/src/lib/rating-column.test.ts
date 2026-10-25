import { fireEvent, render, screen, within } from '@testing-library/svelte';
import { beforeEach, describe, expect, it } from 'vitest';
import ApplicationTable from '#lib/components/application-table.svelte';
import { ratingColumn } from '#lib/rating-column.js';
import { applications } from '../test-support/fixtures.js';
import WithTooltips from '../test-support/with-tooltips.svelte';

const ratingCells = () =>
  screen
    .getAllByRole('row')
    .filter((row) => within(row).queryAllByRole('cell').length > 1)
    .map((row) => within(row).getAllByRole('cell')[2]!.textContent?.replace(/\s+/g, ' ').trim());

beforeEach(() => localStorage.clear());

describe('rating column', () => {
  it('shows each candidate rating with stars, and sorts highest first with the unrated last either way', async () => {
    const rows = [
      { ...applications[0]!, rating: { state: 'NotRated' as const, scorecards: 0 } },
      { ...applications[1]!, rating: { state: 'Rated' as const, value: 3.7, scorecards: 2 } },
      { ...applications[2]!, rating: { state: 'Rated' as const, value: 4.1, scorecards: 3 } },
    ];
    render(WithTooltips, { props: { component: ApplicationTable, props: { applications: rows, extraColumns: [ratingColumn] } } });

    expect(screen.getByRole('img', { name: '4.1 of 5 stars' })).toBeInTheDocument();
    const header = screen.getByRole('button', { name: /^Rating/ });
    await fireEvent.click(header);
    expect(ratingCells()).toEqual(['4.1 3from 3 scorecards', '3.7 2from 2 scorecards', 'Not rated yet']);
    await fireEvent.click(header);
    expect(ratingCells()).toEqual(['3.7 2from 2 scorecards', '4.1 3from 3 scorecards', 'Not rated yet']);
  });

  it('never shows a number to a reviewer with a scorecard to give', () => {
    const rows = [{ ...applications[0]!, rating: { state: 'AfterYourScorecard' as const, scorecards: 0 } }];
    render(WithTooltips, { props: { component: ApplicationTable, props: { applications: rows, extraColumns: [ratingColumn] } } });

    expect(ratingCells()).toEqual(['Rating after your scorecard']);
  });
});
