import GlobeIcon from '@lucide/svelte/icons/globe';
import UsersIcon from '@lucide/svelte/icons/users';
import { fireEvent, render, screen } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import WithTooltips from '../../test-support/with-tooltips.svelte';
import MetricCard from './metric-card.svelte';
import SummaryCard from './summary-card.svelte';

const points = [4, 7, 6, 9, 8, 10, 7, 11, 12, 10, 9, 16].map((value, index) => ({ label: `Week ${index + 1}`, value }));

describe('summary card', () => {
  it('shows the icon, title, what it counts and a description, on the shadcn card', async () => {
    render(WithTooltips, { props: { component: SummaryCard, props: { icon: GlobeIcon, title: 'Network', count: '2 signals', description: 'A hosting provider network.' } } });

    expect(screen.getByText('Network').closest('[data-slot="card"]')).toHaveClass('shadow-surface', 'rounded-panel');
    expect(screen.getByText('2 signals').closest('[data-slot="card-action"]')).toBeInTheDocument();
    expect(screen.getByText('A hosting provider network.')).toHaveClass('line-clamp-2');
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
    await expectNoAxeViolations(document.body);
  });

  it('is a link when it opens what it counts', () => {
    render(WithTooltips, { props: { component: SummaryCard, props: { icon: GlobeIcon, title: 'Content', count: '1 signal', description: 'Different years of experience.', href: '#content' } } });

    expect(screen.getByRole('link', { name: /Content/ })).toHaveAttribute('href', '#content');
  });
});

describe('metric card', () => {
  it('shows the value and a signed change, green when it is good', async () => {
    render(WithTooltips, { props: { component: MetricCard, props: { icon: UsersIcon, title: 'Applicants this week', value: '16', change: 7, period: 'last week', better: 'up', points, href: '#report' } } });

    const card = screen.getByRole('link', { name: 'Applicants this week: 16, up 7 on last week' });
    expect(card).toHaveAttribute('href', '#report');
    expect(card.querySelector('[data-metric-change]')).toHaveClass('text-status-clear');
    expect(card.querySelector('[data-metric-change]')).toHaveTextContent('+7 vs last week');
    expect(screen.getByText('16')).toHaveClass('text-metric');
    await expectNoAxeViolations(document.body);
  });

  it('turns amber when the change needs a look, decided by the metric', () => {
    render(WithTooltips, { props: { component: MetricCard, props: { icon: UsersIcon, title: 'Overdue now', value: '3', change: 2, period: 'last week', better: 'down', points } } });

    const card = screen.getByRole('group', { name: 'Overdue now: 3, up 2 on last week' });
    expect(card.querySelector('[data-metric-change]')).toHaveClass('text-status-review');
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
  });

  it('counts fewer days as good, and reads the latest point out on focus', async () => {
    render(WithTooltips, { props: { component: MetricCard, props: { icon: UsersIcon, title: 'Time to hire, days', value: '21', change: -3, period: 'last month', better: 'down', points, href: '#report' } } });

    const card = screen.getByRole('link');
    expect(card.querySelector('[data-metric-change]')).toHaveClass('text-status-clear');
    expect(card.querySelector('[data-metric-change]')).toHaveTextContent('-3 vs last month');

    await fireEvent.focus(card);
    expect(card.querySelector('[data-metric-readout]')).toHaveTextContent('Week 12: 16');
    await fireEvent.blur(card);
    expect(card.querySelector('[data-metric-readout]')).toBeNull();
  });

  it('reads a point out on focus and on tap when the card has no report to open', async () => {
    render(WithTooltips, { props: { component: MetricCard, props: { icon: UsersIcon, title: 'Overdue now', value: '3', change: 2, period: 'last week', better: 'down', points } } });

    const card = screen.getByRole('group', { name: 'Overdue now: 3, up 2 on last week' });
    expect(card).toHaveAttribute('tabindex', '0');
    await fireEvent.focus(card);
    expect(card.querySelector('[data-metric-readout]')).toHaveTextContent('Week 12: 16');
    await fireEvent.blur(card);

    expect(card.querySelector('[data-metric-readout]')).toBeNull();
    // jsdom has no layout, so a tap lands on the last point.
    await fireEvent.pointerDown(card.querySelector('div.h-12')!, { clientX: 0 });
    expect(card.querySelector('[data-metric-readout]')).toHaveTextContent('Week 12: 16');
  });
});
