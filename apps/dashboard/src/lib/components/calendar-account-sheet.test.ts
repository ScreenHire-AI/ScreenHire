import type { CalendarStatus } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import { apiStub, user } from '../../test-support/fixtures.js';
import WithQueryClient from '../../test-support/with-query-client.svelte';
import CalendarAccountSheet from './calendar-account-sheet.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/jobs') }, navigating: { to: null } }));

const sandbox: CalendarStatus = {
  sandbox: true,
  providers: [{ id: 'Sandbox', name: 'Sandbox calendar' }],
  connection: null,
};

function renderSheet(status: CalendarStatus, routes: Parameters<typeof apiStub>[0] = {}, outcome = '') {
  const fetchMock = vi.fn(apiStub(routes));
  vi.stubGlobal('fetch', fetchMock);
  render(WithQueryClient, { props: { component: CalendarAccountSheet, props: { open: true, status, outcome } } });
  return fetchMock;
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('Calendar account sheet', () => {
  it('offers a button for each provider this ScreenHire has, and sends the browser to its consent page', async () => {
    const assign = vi.fn();
    vi.stubGlobal('location', { ...window.location, assign });
    const fetchMock = renderSheet(
      { ...sandbox, providers: [{ id: 'Google', name: 'Google Calendar' }, { id: 'Sandbox', name: 'Sandbox calendar' }] },
      { 'POST /v1/calendar/connect': () => Response.json({ authorizeUrl: 'http://dashboard.test/v1/calendar/sandbox/authorize?state=x' }) },
    );

    expect(await screen.findByRole('button', { name: 'Connect Google Calendar' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Outlook/ })).not.toBeInTheDocument();
    await fireEvent.click(screen.getByRole('button', { name: 'Connect Sandbox calendar' }));

    await waitFor(() => expect(assign).toHaveBeenCalledWith('http://dashboard.test/v1/calendar/sandbox/authorize?state=x'));
    const call = fetchMock.mock.calls.find(([url]) => String(url) === '/v1/calendar/connect')!;
    expect(JSON.parse(String(call[1]!.body))).toEqual({ provider: 'Sandbox', returnUrl: '/jobs' });
    await expectNoAxeViolations();
  });

  it('shows the connected provider and account, and disconnects', async () => {
    const disconnected = vi.fn(() => new Response(null, { status: 204 }));
    renderSheet(
      {
        ...sandbox,
        connection: {
          provider: 'Sandbox',
          providerName: 'Sandbox calendar',
          accountEmail: 'avery@example.com',
          connectedAt: '2026-11-01T09:00:00Z',
          problem: null,
        },
      },
      { 'DELETE /v1/calendar': disconnected },
    );

    expect(await screen.findByText('avery@example.com')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Connect/ })).not.toBeInTheDocument();
    await fireEvent.click(screen.getByRole('button', { name: 'Disconnect' }));

    await waitFor(() => expect(disconnected).toHaveBeenCalled());
    expect(await screen.findByText('Your calendar is disconnected.')).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('says to connect again when the calendar stopped working, and offers to', async () => {
    renderSheet({
      ...sandbox,
      connection: { provider: 'Sandbox', providerName: 'Sandbox calendar', accountEmail: null, connectedAt: '2026-11-01T09:00:00Z', problem: 'NeedsReconnect' },
    });

    expect(await screen.findByText(/no longer accepts ScreenHire/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Connect again' })).toBeInTheDocument();
  });

  it('shows why the last round trip failed', async () => {
    renderSheet(sandbox, {}, 'Your calendar was not connected: it was cancelled on the calendar.');

    expect(await screen.findByText(/it was cancelled on the calendar/)).toBeInTheDocument();
  });
});
