import { fireEvent, render, screen, within } from '@testing-library/svelte';
import { describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import WithTooltips from '../../test-support/with-tooltips.svelte';
import type { TrayItem } from '../notifications.svelte.js';
import NotificationTray from './notification-tray.svelte';

const items: TrayItem[] = [
  {
    toast: true,
    id: 'change:1',
    kind: 'Moves',
    sentence: ['Jordan Ellis moved ', 'Taylor Brook', ' to Interview'],
    href: '/applications/app-1',
    context: 'Platform Engineer',
  },
  { id: 'mention:note-1', kind: 'mention', title: 'Casey Morgan, Platform Engineer', detail: 'Jordan Ellis mentioned you in a note.', href: '/applications/app-2' },
];

describe('notification tray', () => {
  it('says who changed what, with the record as its link, in a polite region beside the cards', async () => {
    render(WithTooltips, { props: { component: NotificationTray, props: { items, ondismiss: () => {} } } });

    const region = screen.getByRole('region', { name: 'Notifications' });
    expect(region.querySelector('[aria-live="polite"]')).not.toBeNull();
    const rows = within(region).getAllByRole('listitem');
    expect(rows[0]).toHaveTextContent('Jordan Ellis moved Taylor Brook to Interview');
    expect(rows[0]).toHaveTextContent('Platform Engineer');
    expect(within(rows[0]!).getByRole('link', { name: 'Taylor Brook' })).toHaveAttribute('href', '/applications/app-1');
    await expectNoAxeViolations(region);
  });

  it('holds toasts while the pointer or the focus is on the tray, and dismisses one', async () => {
    const onhold = vi.fn();
    const ondismiss = vi.fn();
    render(WithTooltips, { props: { component: NotificationTray, props: { items, ondismiss, onhold } } });
    const row = screen.getAllByRole('listitem')[0]!;

    await fireEvent.pointerEnter(row);
    expect(onhold).toHaveBeenLastCalledWith('pointer', true);
    await fireEvent.pointerLeave(row);
    expect(onhold).toHaveBeenLastCalledWith('pointer', false);
    within(row).getByRole('link').focus();
    expect(onhold).toHaveBeenLastCalledWith('focus', true);

    await fireEvent.click(within(row).getByRole('button', { name: 'Dismiss notification' }));
    expect(ondismiss).toHaveBeenCalledWith('change:1');
  });
});
