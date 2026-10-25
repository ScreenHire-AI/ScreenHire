import { render, screen, within } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import DetailsRailHarness from '../../test-support/details-rail-harness.svelte';

describe('details rail', () => {
  it('is one surface of titled sections, on the right from lg and before the content below it', async () => {
    render(DetailsRailHarness);

    const rail = screen.getByRole('complementary', { name: 'Details' });
    expect(rail).toHaveClass('shadow-surface', 'rounded-panel');
    expect(within(rail).getByRole('region', { name: 'Details' })).toBeInTheDocument();
    expect(within(rail).getByRole('region', { name: 'Tags' })).toBeInTheDocument();

    // Rail first in the reading order, so it folds under the header on one column; the grid moves it right from lg.
    const columns = rail.parentElement!.parentElement!;
    expect(columns).toHaveClass('lg:grid-cols-[minmax(0,1fr)_var(--container-rail)]');
    expect(columns.firstElementChild).toContainElement(rail);
    expect(rail.parentElement).toHaveClass('lg:col-start-2');
    expect(screen.getByRole('region', { name: 'Overview' }).parentElement).toHaveClass('lg:col-start-1');
    await expectNoAxeViolations(document.body);
  });
});
