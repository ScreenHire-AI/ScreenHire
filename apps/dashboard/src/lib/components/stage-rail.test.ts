import { render, screen, within } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import type { Tracker } from '#lib/pipeline-tracker.js';
import { stubViewport } from '../../test-support/viewport.js';
import WithTooltips from '../../test-support/with-tooltips.svelte';
import StageRail from './stage-rail.svelte';

const tracker = (overrides: Partial<Tracker> = {}): Tracker => ({
  steps: [
    { id: 'applied', name: 'Applied', state: 'passed' },
    { id: 'screening', name: 'Screening', state: 'current', note: 'Here 3 days' },
    { id: 'interview', name: 'Interview', state: 'upcoming' },
    { id: 'offer', name: 'Offer', state: 'upcoming' },
  ],
  position: 2,
  total: 4,
  ...overrides,
});

const show = (value: Tracker, overdueAt?: string | null) => render(WithTooltips, { props: { component: StageRail, props: { tracker: value, overdueAt } } });

describe('stage rail', () => {
  it('is a list of every stage in order, the current one marked and the others not', () => {
    show(tracker());

    const rail = screen.getByRole('list', { name: 'Pipeline' });
    const steps = within(rail).getAllByRole('listitem');
    expect(steps.map((step) => step.getAttribute('data-state'))).toEqual(['passed', 'current', 'upcoming', 'upcoming']);
    expect(steps.map((step) => step.getAttribute('aria-current'))).toEqual([null, 'step', null, null]);
    // Screen readers hear the place of each stage.
    expect(steps[1]).toHaveTextContent('Screening, step 2 of 4');
  });

  it('says "Here 3 days" under the current stage only, with no date under any other (W7)', () => {
    show(tracker());

    const steps = within(screen.getByRole('list', { name: 'Pipeline' })).getAllByRole('listitem');
    expect(within(steps[1]!).getByText('Here 3 days')).toBeInTheDocument();
    for (const step of [steps[0]!, steps[2]!, steps[3]!]) {
      expect(step.textContent).not.toMatch(/Here|Oct|Nov|\d{4}/);
    }
  });

  it('fills a check in the marker of a passed stage, and draws no check elsewhere', () => {
    show(tracker());

    const steps = within(screen.getByRole('list', { name: 'Pipeline' })).getAllByRole('listitem');
    expect(steps[0]!.querySelector('svg')).not.toBeNull();
    expect(steps[1]!.querySelector('svg')).toBeNull();
    expect(steps[2]!.querySelector('svg')).toBeNull();
  });

  it('shows the Overdue badge beside "Here 3 days" once the time limit passed, and nothing within it', () => {
    const { unmount } = show(tracker(), new Date(Date.now() - 5 * 3_600_000 - 60_000).toISOString());
    expect(screen.getByText('Overdue by 5 hours')).toBeInTheDocument();
    unmount();

    show(tracker(), new Date(Date.now() + 3 * 86_400_000).toISOString());
    expect(screen.queryByText(/Overdue/)).toBeNull();
  });

  it('keeps how and when they left on the stage they left from, and no Overdue', () => {
    show(
      tracker({
        steps: [
          { id: 'applied', name: 'Applied', state: 'passed' },
          { id: 'screening', name: 'Screening', state: 'left', note: 'Rejected Oct 31' },
          { id: 'interview', name: 'Interview', state: 'upcoming' },
        ],
        total: 3,
        outcome: 'Rejected',
      }),
      new Date(Date.now() - 86_400_000).toISOString(),
    );

    const steps = within(screen.getByRole('list', { name: 'Pipeline' })).getAllByRole('listitem');
    expect(steps[1]).toHaveAttribute('data-state', 'left');
    expect(within(steps[1]!).getByText('Rejected Oct 31')).toBeInTheDocument();
    expect(screen.queryByText(/Overdue/)).toBeNull();
  });

  it('says where they stand in a line when no stage can be marked', () => {
    show(
      tracker({
        steps: [
          { id: 'applied', name: 'Applied', state: 'upcoming' },
          { id: 'screening', name: 'Screening', state: 'upcoming' },
        ],
        position: 0,
        total: 2,
        standing: 'Phone call, here 3 days',
      }),
    );

    expect(screen.getByText('Phone call, here 3 days')).toBeInTheDocument();
  });

  it('shows the current stage with its place on a phone, and the other stages only to screen readers', () => {
    stubViewport(390);
    show(tracker());

    const steps = within(screen.getByRole('list', { name: 'Pipeline' })).getAllByRole('listitem');
    expect(steps[1]).not.toHaveClass('@max-xl:sr-only');
    expect(steps[0]).toHaveClass('@max-xl:sr-only');
    expect(steps[2]).toHaveClass('@max-xl:sr-only');
    expect(within(steps[1]!).getByText('2 of 4')).toBeInTheDocument();
  });
});
