import { render, screen } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import StepMarkers from './step-markers.svelte';

const steps = [
  { label: 'Details', state: 'done' },
  { label: 'Questions', state: 'current' },
  { label: 'Publish', state: 'upcoming' },
] as const;

describe('step markers', () => {
  it('lists the steps in order, marking the current one and saying which are done', async () => {
    const { container } = render(StepMarkers, { props: { label: 'New job', steps } });

    const list = screen.getByRole('list', { name: 'New job' });
    const items = screen.getAllByRole('listitem');
    expect(list).toContainElement(items[0]!);
    expect(items.map((item) => item.textContent?.replace(/\s+/g, ' ').trim())).toEqual(['Details, done', '2 Questions', '3 Publish']);
    expect(items[1]).toHaveAttribute('aria-current', 'step');
    expect(items[0]).not.toHaveAttribute('aria-current');
    await expectNoAxeViolations(container);
  });

  it('draws square markers joined by a line, never a count pill (T4)', () => {
    const { container } = render(StepMarkers, { props: { label: 'New job', steps } });

    const markers = [...container.querySelectorAll('[data-state]')];
    expect(markers).toHaveLength(3);
    for (const marker of markers) {
      expect(marker).toHaveClass('size-7', 'rounded-step', 'border', 'bg-card');
      expect(marker).not.toHaveClass('rounded-full');
    }
    expect(container.querySelectorAll('li > span[aria-hidden="true"]')).toHaveLength(2);
  });
});
