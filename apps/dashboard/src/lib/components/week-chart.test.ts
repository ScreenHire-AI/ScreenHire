import { fireEvent, render, screen, within } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import WeekChart, { type WeekPoint } from '#lib/components/week-chart.svelte';
import { expectNoAxeViolations } from '../../test-support/axe.js';

const week = (key: string, applications: number, hires: number): WeekPoint => ({
  key,
  label: key,
  applications,
  hires,
  summary: `Week of ${key}: ${applications} applications, ${hires} hires`,
});

const weeks = [week('Oct 5', 4, 0), week('Oct 12', 8, 2), week('Oct 19', 0, 0)];

describe('week chart', () => {
  it('pairs applications and hires per week, scaled to the largest, with a legend', async () => {
    const { container } = render(WeekChart, { props: { label: 'Applications and hires by week', weeks } });

    expect(within(screen.getByRole('list', { name: 'Legend' })).getAllByRole('listitem').map((item) => item.textContent?.trim())).toEqual(['Applications', 'Hires']);
    const columns = container.querySelectorAll<HTMLElement>('[data-week]');
    expect(columns).toHaveLength(3);
    const [applications, hires] = columns[1]!.querySelectorAll<HTMLElement>('span');
    expect(applications).toHaveClass('bg-chart-2');
    expect(applications!.style.height).toBe('max(2px, 100%)');
    expect(hires).toHaveClass('bg-chart-1');
    expect(hires!.style.height).toBe('max(2px, 25%)');
    // A zero draws nothing.
    expect(columns[2]!.querySelector<HTMLElement>('span')!.style.height).toBe('0px');
    await expectNoAxeViolations();
  });

  it('reads the latest week out first, then the one pointed at or reached with the arrow keys', async () => {
    const { container } = render(WeekChart, { props: { label: 'Applications and hires by week', weeks } });
    const readout = container.querySelector('[data-week-readout]')!;
    expect(readout).toHaveTextContent('Week of Oct 19: 0 applications, 0 hires');

    await fireEvent.pointerEnter(container.querySelectorAll('[data-week]')[1]!);
    expect(readout).toHaveTextContent('Week of Oct 12: 8 applications, 2 hires');

    const chart = screen.getByRole('group', { name: 'Applications and hires by week' });
    chart.focus();
    await fireEvent.pointerLeave(chart);
    await fireEvent.keyDown(chart, { key: 'Home' });
    expect(readout).toHaveTextContent('Week of Oct 5: 4 applications, 0 hires');
    await fireEvent.keyDown(chart, { key: 'ArrowRight' });
    expect(readout).toHaveTextContent('Week of Oct 12');
    await fireEvent.keyDown(chart, { key: 'End' });
    expect(readout).toHaveTextContent('Week of Oct 19');
  });
});
