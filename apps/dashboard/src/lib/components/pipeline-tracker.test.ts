import { render, screen, within } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import type { Tracker } from '../pipeline-tracker.js';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import WithTooltips from '../../test-support/with-tooltips.svelte';
import PipelineTracker from './pipeline-tracker.svelte';

const names = ['Applied', 'Screening', 'Interview', 'Offer', 'Hired'];

const tracker = (states: Tracker['steps'][number]['state'][], notes: (string | undefined)[], outcome?: string): Tracker => {
  const steps = names.map((name, index) => ({ id: name.toLowerCase(), name, state: states[index]!, note: notes[index] }));
  const position = steps.findIndex((step) => step.state === 'current' || step.state === 'left') + 1;
  return { steps, position, total: steps.length, outcome };
};

const inPipeline = tracker(['passed', 'current', 'upcoming', 'upcoming', 'upcoming'], ['Oct 20', 'Here 3 days']);

const show = (value: Tracker, overdueAt?: string) => render(WithTooltips, { props: { component: PipelineTracker, props: { tracker: value, overdueAt } } });

describe('pipeline tracker', () => {
  it('lists every stage in order, each with a dot, and marks the current one as the current step', async () => {
    const { container } = show(inPipeline);

    const items = within(screen.getByRole('list', { name: 'Pipeline' })).getAllByRole('listitem');
    expect(items).toHaveLength(5);
    expect(items.map((item) => item.getAttribute('data-state'))).toEqual(['passed', 'current', 'upcoming', 'upcoming', 'upcoming']);
    expect(items[1]).toHaveAttribute('aria-current', 'step');
    expect(items.filter((item) => item.hasAttribute('aria-current'))).toHaveLength(1);
    expect(container.querySelectorAll('li > span:first-child > span.rounded-full')).toHaveLength(5);
    await expectNoAxeViolations(container);
  });

  it('tells screen readers "Screening, step 2 of 5" and shows the date reached and how long they have been here', () => {
    show(inPipeline);

    const items = screen.getAllByRole('listitem');
    expect(items[1]).toHaveTextContent(/^Screening, step 2 of 5.*Here 3 days$/);
    expect(items[0]).toHaveTextContent(/^Applied, step 1 of 5.*Oct 20$/);
    expect(items[2]).toHaveTextContent(/^Interview, step 3 of 5$/);
  });

  it('fills the passed stages, rings the current one and leaves later ones empty', () => {
    const { container } = show(inPipeline);

    const dots = [...container.querySelectorAll('li > span:first-child > span.rounded-full')];
    expect(dots[0]).toHaveClass('bg-primary');
    expect(dots[1]).toHaveClass('bg-card', 'border-primary', 'outline-2');
    expect(dots[2]).toHaveClass('bg-card', 'border-muted-foreground');
    expect(dots[2]).not.toHaveClass('bg-primary');
  });

  it('draws the stage they left from as a dark dot with how they left', () => {
    const saved = tracker(['passed', 'passed', 'left', 'upcoming', 'upcoming'], ['Oct 20', 'Oct 22', 'Saved Oct 31'], 'Saved');
    const { container } = show(saved);

    const items = screen.getAllByRole('listitem');
    expect(items[2]).toHaveTextContent(/^Interview, step 3 of 5.*Saved Oct 31$/);
    expect(items[2]).not.toHaveAttribute('aria-current');
    expect(container.querySelectorAll('li')[2]!.querySelector('span.rounded-full')).toHaveClass('bg-foreground');
  });

  it('keeps every stage for screen readers but shows only the current one, with "2 of 5", in a narrow container', () => {
    show(inPipeline);

    const items = screen.getAllByRole('listitem');
    expect(items[1]).not.toHaveClass('@max-xl:sr-only');
    for (const index of [0, 2, 3, 4]) expect(items[index]).toHaveClass('@max-xl:sr-only');
    const count = within(items[1]!).getByText('2 of 5');
    expect(count).toHaveClass('@xl:hidden');
    expect(count).toHaveAttribute('aria-hidden', 'true');
  });

  it('shows the Overdue badge beside the current stage once its time limit has passed, and nowhere else', () => {
    show(inPipeline, '2026-01-01T00:00:00Z');

    expect(screen.getAllByText(/^Overdue/)).toHaveLength(1);
    expect(within(screen.getAllByRole('listitem')[1]!).getByText(/^Overdue/)).toBeInTheDocument();
  });

  it('shows no Overdue badge within the time limit', () => {
    show(inPipeline, '2999-01-01T00:00:00Z');

    expect(screen.queryByText(/^Overdue/)).not.toBeInTheDocument();
  });

  it('draws every stage empty and says where they stand, outside the stages, when no stage can be marked', async () => {
    const lost = { ...tracker(['upcoming', 'upcoming', 'upcoming', 'upcoming', 'upcoming'], [], 'Rejected'), standing: 'Rejected Oct 31' };
    const { container } = show(lost);

    expect(screen.getAllByRole('listitem').every((item) => item.getAttribute('data-state') === 'upcoming')).toBe(true);
    expect(container.querySelector('[aria-current]')).toBeNull();
    // Every stage is hidden in a narrow container, so this line sits outside them to stay on a phone.
    const line = screen.getByText('Rejected Oct 31');
    expect(line.closest('ol')).toBeNull();
    await expectNoAxeViolations(container);
  });

  it('keeps the Overdue badge on that line while they are in the pipeline, and drops it once they left', () => {
    const empty = tracker(['upcoming', 'upcoming', 'upcoming', 'upcoming', 'upcoming'], []);
    const { unmount } = show({ ...empty, standing: 'Phone call, here 3 days' }, '2026-01-01T00:00:00Z');
    expect(screen.getByText('Phone call, here 3 days').parentElement).toHaveTextContent(/Overdue/);
    unmount();

    show({ ...empty, outcome: 'Rejected', standing: 'Rejected Oct 31' }, '2026-01-01T00:00:00Z');
    expect(screen.queryByText(/^Overdue/)).not.toBeInTheDocument();
  });

  it('adds no line when a stage shows where they stand', () => {
    const { container } = show(inPipeline);

    expect(container.querySelector('ol + p')).toBeNull();
  });
});
