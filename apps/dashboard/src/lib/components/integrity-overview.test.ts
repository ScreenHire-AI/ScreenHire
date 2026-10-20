import type { ApplicationSummary } from '@screenhire/api-client';
import { fireEvent, render, screen, within } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { noIntegrityFilter, type IntegrityFilter } from '#lib/integrity.js';
import { applications } from '../../test-support/fixtures.js';
import { summary } from '../../test-support/integrity-fixtures.js';
import IntegrityOverview from './integrity-overview.svelte';

const rows: ApplicationSummary[] = [
  { ...applications[0]!, integrity: summary() },
  { ...applications[0]!, id: 'b', integrity: summary() },
  { ...applications[1]!, integrity: summary({ authenticity: 'ReviewSuggested', authenticityReasons: ['A', 'B'] }) },
  { ...applications[2]!, integrity: summary({ authenticity: 'VerificationRecommended', authenticityReasons: ['A'], authenticityDismissed: true }) },
  { ...applications[2]!, id: 'p', integrity: null },
];

function legend() {
  return within(screen.getByRole('list', { name: 'Authenticity levels' }))
    .getAllByRole('button')
    .map((button) => button.textContent?.replace(/\s+/g, ' ').trim());
}

describe('integrity overview', () => {
  it('shows the applicant count, a number per level in words and marks, and a stacked bar hidden from screen readers', () => {
    const { container } = render(IntegrityOverview, { props: { applications: rows, filter: { ...noIntegrityFilter } } });

    expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent('5 applicants · authenticity');
    expect(legend()).toEqual(['2 No concerns', '1 Review suggested', '1 Verification recommended', '1 Checks pending']);
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
    let filter: IntegrityFilter = { ...noIntegrityFilter };
    const { rerender } = render(IntegrityOverview, {
      props: {
        applications: rows,
        get filter() {
          return filter;
        },
        set filter(value) {
          filter = value;
        },
      },
    });

    const review = screen.getByRole('button', { name: /Review suggested/ });
    await fireEvent.click(review);
    expect(filter).toEqual({ authenticity: 'ReviewSuggested', aiAssistance: null });
    await rerender({ applications: rows, filter });
    expect(screen.getByRole('button', { name: /Review suggested/ })).toHaveAttribute('aria-pressed', 'true');

    await fireEvent.click(screen.getByRole('button', { name: /Review suggested/ }));
    expect(filter.authenticity).toBeNull();
  });

  it('leaves out the pending entry once every application is assessed', () => {
    render(IntegrityOverview, { props: { applications: rows.slice(0, 4), filter: { ...noIntegrityFilter } } });

    expect(legend()).toEqual(['2 No concerns', '1 Review suggested', '1 Verification recommended']);
  });
});
