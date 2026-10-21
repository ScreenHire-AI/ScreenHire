import MessageSquareIcon from '@lucide/svelte/icons/message-square';
import { fireEvent, render, screen, within } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import WithTooltips from '../../test-support/with-tooltips.svelte';
import ActivityList from './activity-list.svelte';
import RelativeTime from './relative-time.svelte';

const now = Date.parse('2026-10-20T12:00:00Z');
const tooltip = () => document.querySelector('[data-slot="tooltip-content"]');

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(now);
});

afterEach(() => {
  vi.useRealTimers();
});

describe('activity list', () => {
  it('shows each event with its actor, its note and the time ago, newest first as given', async () => {
    const { container } = render(WithTooltips, {
      props: {
        component: ActivityList,
        props: {
          label: 'Activity',
          items: [
            { id: 'a', icon: MessageSquareIcon, event: 'Note added', actor: 'Avery Quinn', note: 'Strong portfolio.', at: '2026-10-20T10:00:00Z' },
            { id: 'b', icon: MessageSquareIcon, event: 'Applied', at: '2026-10-10T12:00:00Z' },
          ],
        },
      },
    });

    const items = within(screen.getByRole('list', { name: 'Activity' })).getAllByRole('listitem');
    expect(items[0]).toHaveTextContent('Note added Avery Quinn');
    expect(items[0]).toHaveTextContent('"Strong portfolio."');
    expect(within(items[0]!).getByText('2 hours ago')).toHaveAttribute('datetime', '2026-10-20T10:00:00Z');
    // Older than a week: the date (section 13).
    expect(within(items[1]!).getByText('Oct 10, 2026')).toBeInTheDocument();
    // The icon sits in a circle and is decoration.
    expect(items[0]!.querySelector('svg')!.parentElement).toHaveClass('rounded-full');
    expect(items[0]!.querySelector('svg')!.parentElement).toHaveAttribute('aria-hidden', 'true');
    await expectNoAxeViolations(container);
  });

  it('shows skeleton rows of the same shape while loading (E1)', () => {
    render(ActivityList, { props: { label: 'Activity', items: undefined } });

    expect(screen.queryByRole('list')).not.toBeInTheDocument();
    const skeleton = document.querySelector('[data-activity-skeleton]')!;
    expect(skeleton).toHaveAttribute('aria-hidden', 'true');
    expect(skeleton.querySelectorAll('.rounded-full')).toHaveLength(2);
  });
});

describe('relative time', () => {
  it('shows the exact date and time on focus and on tap, not only on hover (F4)', async () => {
    render(WithTooltips, { props: { component: RelativeTime, props: { at: '2026-10-20T10:00:00Z' } } });

    const time = screen.getByText('2 hours ago');
    expect(time).toHaveAttribute('tabindex', '0');
    expect(time).not.toHaveAttribute('type');
    await fireEvent.focus(time);
    // The tooltip describes the time it belongs to.
    expect(document.getElementById(time.getAttribute('aria-describedby')!)).toHaveTextContent('Oct 20, 2026, 10:00 AM');

    await fireEvent.blur(time);
    expect(tooltip()).toBeNull();
    await fireEvent.pointerDown(time);
    await fireEvent.pointerUp(time);
    await fireEvent.click(time);
    expect(tooltip()).toHaveTextContent('Oct 20, 2026, 10:00 AM');
    await expectNoAxeViolations(document.body);
  });
});
