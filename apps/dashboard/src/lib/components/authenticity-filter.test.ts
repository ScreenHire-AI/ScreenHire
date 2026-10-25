import type { ApplicationSummary } from '@screenhire/api-client';
import { fireEvent, render, screen, within } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import { applications } from '../../test-support/fixtures.js';
import { summary } from '../../test-support/integrity-fixtures.js';
import Harness from '../../test-support/authenticity-filter-harness.svelte';

const rows: ApplicationSummary[] = [
  { ...applications[0]!, integrity: summary() },
  { ...applications[0]!, id: 'b', integrity: summary() },
  { ...applications[1]!, integrity: summary({ authenticity: 'ReviewSuggested', authenticityReasons: ['A', 'B'] }) },
  { ...applications[2]!, integrity: summary({ authenticity: 'VerificationRecommended', authenticityReasons: ['A'], authenticityDismissed: true }) },
  { ...applications[2]!, id: 'p', integrity: null },
];

function chips() {
  return within(screen.getByRole('group', { name: 'Authenticity' }))
    .getAllByRole('button')
    .map((button) => button.textContent?.replace(/\s+/g, ' ').trim());
}

describe('authenticity filter', () => {
  it('is a chip per level with its count in words, with no status icon or colour (I2, T1)', async () => {
    const { container } = render(Harness, { props: { applications: rows } });

    expect(chips()).toEqual(['No concerns 2', 'Review suggested 1', 'Verification suggested 1', 'Checks pending 1']);
    expect(screen.getByText('1 dismissed after review')).toBeInTheDocument();
    expect(container.querySelector('[data-status-icon]')).toBeNull();
    expect(container.querySelector('[class*="bg-status-"]')).toBeNull();
    await expectNoAxeViolations(container);
  });

  it('filters by a level, and clears it on a second press', async () => {
    render(Harness, { props: { applications: rows } });
    const filter = () => JSON.parse(screen.getByTestId('filter').textContent!);

    await fireEvent.click(screen.getByRole('button', { name: /Review suggested/ }));
    expect(filter()).toEqual({ authenticity: 'ReviewSuggested', aiAssistance: null });
    expect(screen.getByRole('button', { name: /Review suggested/ })).toHaveAttribute('aria-pressed', 'true');

    await fireEvent.click(screen.getByRole('button', { name: /Review suggested/ }));
    expect(filter().authenticity).toBeNull();
    expect(screen.getByRole('button', { name: /Review suggested/ })).toHaveAttribute('aria-pressed', 'false');
  });

  it('leaves out the pending chip once every application is assessed', () => {
    render(Harness, { props: { applications: rows.slice(0, 4) } });

    expect(chips()).toEqual(['No concerns 2', 'Review suggested 1', 'Verification suggested 1']);
  });

  it('offers the same choices in a filter menu on a phone', async () => {
    render(Harness, { props: { applications: rows, menu: true } });
    const filter = () => JSON.parse(screen.getByTestId('filter').textContent!);

    const trigger = screen.getByRole('button', { name: 'Authenticity: All' });
    await fireEvent.pointerDown(trigger, { button: 0, pointerType: 'mouse' });
    const items = await screen.findAllByRole('menuitemradio');
    expect(items.map((item) => item.textContent?.replace(/\s+/g, ' ').trim())).toEqual([
      'All 5',
      'No concerns 2',
      'Review suggested 1',
      'Verification suggested 1',
      'Checks pending 1',
    ]);

    await fireEvent.click(screen.getByRole('menuitemradio', { name: /Verification suggested/ }));
    expect(filter().authenticity).toBe('VerificationRecommended');
    expect(screen.getByRole('button', { name: 'Authenticity: Verification suggested' })).toBeInTheDocument();
  });
});
