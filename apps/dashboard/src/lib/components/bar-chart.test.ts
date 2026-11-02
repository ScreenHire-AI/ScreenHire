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

  it('links a row\'s name outside the image, so the link stays reachable', async () => {
    const list = renderChart({
      series: [{ label: 'Applications', tone: 'chart-1' }],
      rows: [{ ...row('Interview', [5], '80% moved on'), href: '/candidates?reached=Interview', share: '42%' }, row('Offer', [4], null)],
    });

    const link = within(list).getByRole('link', { name: 'Interview' });
    expect(link).toHaveAttribute('href', '/candidates?reached=Interview');
    expect(link.closest('[role="img"]')).toBeNull();
    // The second row has no link, so its name is not announced twice: the image carries it.
    expect(within(list).getAllByRole('link')).toHaveLength(1);
    expect(within(list).getAllByRole('img')[0]).toHaveTextContent(/5\s*42%\s*80% moved on/);
    await expectNoAxeViolations();
  });

  it('marks the biggest drop in its own row', () => {
    const list = renderChart({
      series: [{ label: 'Applications', tone: 'chart-1' }],
      rows: [
        { ...row('Applied', [10], '90% moved on'), summary: 'Applied: 10 applications, 90% moved on' },
        { ...row('Screening', [9], '40% moved on'), drop: true, summary: 'Screening: 9 applications, 40% moved on, the biggest drop' },
      ],
    });

    const [applied, screening] = within(list).getAllByRole('img');
    expect(screening).toHaveAccessibleName('Screening: 9 applications, 40% moved on, the biggest drop');
    expect(applied).toHaveAccessibleName('Applied: 10 applications, 90% moved on');
    expect(screening.querySelector('svg')).not.toBeNull();
    expect(screening.querySelector('.font-semibold')).toHaveTextContent('40% moved on');
    expect(applied.querySelector('svg')).toBeNull();
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
