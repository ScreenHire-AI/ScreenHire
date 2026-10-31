import type { SlackStatus } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import { apiStub, user } from '../../test-support/fixtures.js';
import WithQueryClient from '../../test-support/with-query-client.svelte';
import SlackAccountSheet from './slack-account-sheet.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/jobs') }, navigating: { to: null } }));

const workspace: SlackStatus = {
  available: true,
  sandbox: true,
  workspace: { teamName: 'ScreenHire sandbox', disconnected: false },
  account: null,
  settings: null,
};

function renderSheet(status: SlackStatus, routes: Parameters<typeof apiStub>[0] = {}) {
  const fetchMock = vi.fn(apiStub(routes));
  vi.stubGlobal('fetch', fetchMock);
  render(WithQueryClient, { props: { component: SlackAccountSheet, props: { open: true, status } } });
  return fetchMock;
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('Slack account sheet', () => {
  it('offers to link an account, coming back to the page it opened on', async () => {
    const assign = vi.fn();
    vi.stubGlobal('location', { ...window.location, assign });
    const fetchMock = renderSheet(workspace, {
      'POST /v1/slack/account/link': () => Response.json({ authorizeUrl: 'http://dashboard.test/v1/slack/sandbox/authorize?kind=link' }),
    });

    await fireEvent.click(await screen.findByRole('button', { name: 'Link my Slack account' }));

    await waitFor(() => expect(assign).toHaveBeenCalledWith('http://dashboard.test/v1/slack/sandbox/authorize?kind=link'));
    const call = fetchMock.mock.calls.find(([url]) => String(url) === '/v1/slack/account/link')!;
    expect(JSON.parse(String(call[1]!.body))).toEqual({ returnUrl: '/jobs' });
    await expectNoAxeViolations();
  });

  it('shows the linked account and saves a switch as it changes', async () => {
    const saved = vi.fn(() => Response.json({ slackName: 'Avery Quinn', linkedAt: '2026-10-30T09:00:00Z', mentions: false, tasks: true, problem: null }));
    renderSheet(
      { ...workspace, account: { slackName: 'Avery Quinn', linkedAt: '2026-10-30T09:00:00Z', mentions: true, tasks: true, problem: null } },
      { 'PUT /v1/slack/account': saved },
    );

    expect(await screen.findByText('Avery Quinn')).toBeInTheDocument();
    const mentions = screen.getByRole('switch', { name: 'Mentions' });
    expect(mentions).toBeChecked();
    expect(screen.getByRole('button', { name: 'Unlink' })).toBeInTheDocument();
    await fireEvent.click(mentions);

    await waitFor(() => expect(saved).toHaveBeenCalled());
    expect(await screen.findByText('Saved.')).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('says an owner or admin reconnects a disconnected workspace, and offers no link', async () => {
    renderSheet({ ...workspace, workspace: { teamName: 'ScreenHire sandbox', disconnected: true } });

    expect(await screen.findByText(/An owner or admin reconnects it in Settings/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Link my Slack account' })).not.toBeInTheDocument();
  });
});
