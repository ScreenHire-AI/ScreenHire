import type { ApplicationSummary } from '@screenhire/api-client';
import { fireEvent, render, screen, within } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { applications } from '../../test-support/fixtures.js';
import { summary } from '../../test-support/integrity-fixtures.js';
import Harness from '../../test-support/integrity-overview-harness.svelte';

const rows: ApplicationSummary[] = [
  { ...applications[0]!, integrity: summary() },
  { ...applications[0]!, id: 'b', integrity: summary() },
  { ...applications[1]!, integrity: summary({ authenticity: 'ReviewSuggested', authenticityReasons: ['A', 'B'] }) },
  { ...applications[2]!, integrity: summary({ authenticity: 'VerificationRecommended', authenticityReasons: ['A'], authenticityDismissed: true }) },
  { ...applications[2]!, id: 'p', integrity: null },
];

function legend() {
  return within(screen.getByRole('group', { name: 'Authenticity levels' }))
    .getAllByRole('button')
    .map((button) => button.textContent?.replace(/\s+/g, ' ').trim());
}

describe('integrity overview', () => {
  it('is a panel with the applicant count, a number per level in words and marks, and a stacked bar hidden from screen readers', () => {
    const { container } = render(Harness, { props: { applications: rows } });

    const panel = screen.getByRole('region', { name: 'Authenticity' });
    expect(within(panel).getByRole('heading', { level: 2 }).parentElement).toHaveTextContent('Authenticity 5');
    expect(legend()).toEqual(['No concerns 2', 'Review suggested 1', 'Verification suggested 1', 'Checks pending 1']);
    expect(screen.getByText('1 dismissed after review')).toBeInTheDocument();
    const bar = container.querySelector('[data-bar]')!.parentElement!;
    expect(bar).toHaveAttribute('aria-hidden', 'true');
    expect([...bar.children].map((part) => [part.getAttribute('data-bar'), (part as HTMLElement).style.flexGrow])).toEqual([
      ['NoConcerns', '2'],
      ['ReviewSuggested', '1'],
      ['VerificationRecommended', '1'],
      ['Pending', '1'],
    ]);
  });

  it('filters the table by a level from its legend, and clears it on a second press', async () => {
    render(Harness, { props: { applications: rows } });
    const filter = () => JSON.parse(screen.getByTestId('filter').textContent!);

    await fireEvent.click(screen.getByRole('button', { name: /Review suggested/ }));
    expect(filter()).toEqual({ authenticity: 'ReviewSuggested', aiAssistance: null });
    expect(screen.getByRole('button', { name: /Review suggested/ })).toHaveAttribute('aria-pressed', 'true');

    await fireEvent.click(screen.getByRole('button', { name: /Review suggested/ }));
    expect(filter().authenticity).toBeNull();
    expect(screen.getByRole('button', { name: /Review suggested/ })).toHaveAttribute('aria-pressed', 'false');
  });

  it('leaves out the pending entry once every application is assessed', () => {
    render(Harness, { props: { applications: rows.slice(0, 4) } });

    expect(legend()).toEqual(['No concerns 2', 'Review suggested 1', 'Verification suggested 1']);
  });
});
