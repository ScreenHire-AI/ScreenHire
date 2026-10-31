import { render, screen, within } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import BarChart, { type BarRow } from '#lib/components/bar-chart.svelte';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import WithQueryClient from '../../test-support/with-query-client.svelte';

const row = (key: string, values: number[], note?: string | null): BarRow => ({
  key,
  label: key,
  values,
  texts: values.map(String),
  note,
  summary: `${key}: ${values.join(' and ')}`,
});

function renderChart(props: { series: { label: string; tone: 'chart-1' | 'chart-2' }[]; rows: BarRow[] }) {
  render(WithQueryClient, { props: { component: BarChart, props: { label: 'Example', ...props } } });
  return screen.getByRole('list', { name: 'Example' });
}

describe('bar chart', () => {
  it('scales each bar to the largest value, writes every value, and draws nothing for zero', async () => {
    const list = renderChart({ series: [{ label: 'Applications', tone: 'chart-1' }], rows: [row('Applied', [8], '63% moved on'), row('Hired', [0], null)] });

    const [applied, hired] = within(list).getAllByRole('img');
    expect(applied).toHaveAccessibleName('Applied: 8');
    expect(applied).toHaveTextContent('63% moved on');
    const bars = list.querySelectorAll<HTMLElement>('.bg-chart-1');
    expect(bars[0]!.style.width).toBe('max(2px, 1 * (100% - 5.5rem))');
    expect(bars[1]!.style.width).toBe('0px');
    expect(hired).toHaveTextContent('0');
    // One series needs no legend: the panel's title names it.
    expect(screen.queryByRole('list', { name: 'Legend' })).toBeNull();
    await expectNoAxeViolations();
  });

  it('pairs two series with a legend, each in its own colour', async () => {
    const list = renderChart({
      series: [
        { label: 'Applications', tone: 'chart-2' },
        { label: 'Hires', tone: 'chart-1' },
      ],
      rows: [row('Careers page', [6, 1])],
    });

    expect(within(screen.getByRole('list', { name: 'Legend' })).getAllByRole('listitem').map((item) => item.textContent?.trim())).toEqual(['Applications', 'Hires']);
    expect(list.querySelectorAll('.bg-chart-2')).toHaveLength(1);
    expect(list.querySelectorAll('.bg-chart-1')).toHaveLength(1);
    await expectNoAxeViolations();
  });
});
