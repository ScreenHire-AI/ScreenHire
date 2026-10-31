import { fireEvent, render, screen, waitFor } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import { apiStub } from '../../test-support/fixtures.js';
import WithQueryClient from '../../test-support/with-query-client.svelte';
import NotificationSettingsSheet from './notification-settings-sheet.svelte';

const json = (body: unknown) => new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } });

afterEach(() => vi.unstubAllGlobals());

describe('notifications sheet', () => {
  it("shows the person's settings, hides the kinds while toasts are off, and saves what they chose", async () => {
    const saved: unknown[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn(
        apiStub({
          'GET /v1/notification-settings': () => json({ toasts: true, closeToasts: true, off: ['Tags'] }),
          'PUT /v1/notification-settings': (init) => {
            saved.push(JSON.parse(String(init?.body)));
            return json({ toasts: true, closeToasts: false, off: ['Tags', 'Moves'] });
          },
        }),
      ),
    );
    const onsaved = vi.fn();
    render(WithQueryClient, { props: { component: NotificationSettingsSheet, props: { open: true, onsaved } } });

    const dialog = await screen.findByRole('dialog', { name: 'Notifications' });
    const moves = await screen.findByRole('switch', { name: 'Moves' });
    expect(moves).toBeChecked();
    expect(screen.getByRole('switch', { name: 'Tags' })).not.toBeChecked();
    await expectNoAxeViolations(dialog);

    await fireEvent.click(screen.getByRole('switch', { name: 'Show toasts' }));
    expect(screen.queryByRole('switch', { name: 'Moves' })).toBeNull();
    await fireEvent.click(screen.getByRole('switch', { name: 'Show toasts' }));

    await fireEvent.click(screen.getByRole('switch', { name: 'Moves' }));
    await fireEvent.click(screen.getByRole('switch', { name: 'Close on their own' }));
    await fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(onsaved).toHaveBeenCalledWith('Your notification settings were saved.'));
    expect(saved).toEqual([{ toasts: true, closeToasts: false, off: ['Tags', 'Moves'] }]);
  });
});
