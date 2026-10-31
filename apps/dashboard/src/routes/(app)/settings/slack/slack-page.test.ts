import type { SlackChannelOption, SlackStatus } from '@screenhire/api-client';
import { fireEvent, render, screen, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import { apiStub, user } from '../../../../test-support/fixtures.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

const location = vi.hoisted(() => ({ url: new URL('http://dashboard.test/settings/slack') }));
vi.mock('$app/state', () => ({
  page: {
    data: { user },
    get url() {
      return location.url;
    },
  },
  navigating: { to: null },
}));
vi.mock('$app/navigation', async (original) => ({ ...(await original<object>()), goto: vi.fn(() => Promise.resolve()) }));

// jsdom has no layout, so the channel list cannot scroll its highlighted item into view.
Element.prototype.scrollIntoView = () => {};

const connected = (overrides: Partial<NonNullable<SlackStatus['settings']>> = {}): SlackStatus => ({
  available: true,
  sandbox: true,
  workspace: { teamName: 'ScreenHire sandbox', disconnected: overrides.problem === 'Disconnected' },
  account: null,
  settings: {
    channel: { id: 'C1', name: 'hiring' },
    events: ['ApplicationSubmitted'],
    connectedAt: '2026-10-29T09:00:00Z',
    connectedBy: 'Avery Quinn',
    problem: null,
    problemAt: null,
    ...overrides,
  },
});

const channels: SlackChannelOption[] = [
  { id: 'C1', name: 'hiring', isPrivate: false },
  { id: 'C2', name: 'recruiting', isPrivate: true },
];

function renderPage(status: SlackStatus, routes: Parameters<typeof apiStub>[0] = {}) {
  vi.stubGlobal(
    'fetch',
    vi.fn(
      apiStub({
        'GET /v1/slack': () => Response.json(status),
        'GET /v1/slack/channels': () => Response.json(channels),
        ...routes,
      }),
    ),
  );
  render(WithQueryClient, { props: { component: Page, props: {} } });
}

afterEach(() => {
  vi.unstubAllGlobals();
  location.url = new URL('http://dashboard.test/settings/slack');
});

describe('Slack page', () => {
  it('says Slack is not set up here and offers nothing when this ScreenHire has no Slack app', async () => {
    renderPage({ available: false, sandbox: false, workspace: null, account: null, settings: null });

    expect(await screen.findByText('Slack is not set up here')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Add to Slack/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'More actions' })).not.toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('offers Add to Slack, the one primary action, before a workspace is connected', async () => {
    renderPage({ available: true, sandbox: true, workspace: null, account: null, settings: null });

    expect(await screen.findByRole('button', { name: 'Add to Slack' })).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('says why a round trip to Slack failed', async () => {
    location.url = new URL('http://dashboard.test/settings/slack?slack=denied');
    renderPage({ available: true, sandbox: true, workspace: null, account: null, settings: null });

    expect(await screen.findByText('Slack was not connected: it was cancelled on Slack.')).toBeInTheDocument();
  });

  it('shows the connected workspace, its channel and events, and says verification results stay in ScreenHire', async () => {
    renderPage(connected());

    expect(await screen.findByText('#hiring')).toBeInTheDocument();
    // The workspace and when it was added are labelled facts in the header band (W3).
    expect(screen.getByText('Workspace', { selector: 'dt' }).nextElementSibling).toHaveTextContent('ScreenHire sandbox');
    expect(screen.getByText('Added', { selector: 'dt' }).nextElementSibling).toHaveTextContent('by Avery Quinn');
    expect(screen.getByText('Connected')).toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: 'New application' })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'Job published' })).not.toBeChecked();
    expect(screen.getByText(/Verification results stay in ScreenHire/)).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('saves a new channel and events', async () => {
    const saved = vi.fn((init: RequestInit | undefined) => {
      void init;
      return Response.json(connected({ channel: { id: 'C2', name: 'recruiting' }, events: ['ApplicationSubmitted', 'JobPublished'] }));
    });
    renderPage(connected(), { 'PUT /v1/slack/channel': saved });

    await fireEvent.click(await screen.findByRole('button', { name: 'Change channel' }));
    await fireEvent.click(await screen.findByRole('option', { name: /recruiting/ }));
    await fireEvent.click(screen.getByRole('checkbox', { name: 'Job published' }));
    await fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByText('Saved.')).toBeInTheDocument();
    expect(JSON.parse(String(saved.mock.calls[0]![0]!.body))).toEqual({ channelId: 'C2', events: ['ApplicationSubmitted', 'JobPublished'] });
  });

  it('marks a problem beside the title and says how to fix it', async () => {
    renderPage(connected({ problem: 'NotInChannel', problemAt: '2026-10-30T09:00:00Z' }));

    expect(await screen.findByText('Not posting')).toBeInTheDocument();
    expect(screen.getByText(/Invite the app to it in Slack/)).toBeInTheDocument();
  });

  it('offers Reconnect once, in the header, once Slack no longer accepts ScreenHire', async () => {
    renderPage(connected({ problem: 'Disconnected', problemAt: '2026-10-30T09:00:00Z' }));

    expect(await screen.findByText('Disconnected')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Reconnect' })).toBeInTheDocument();
    await fireEvent.pointerDown(screen.getByRole('button', { name: 'More actions' }), { button: 0, pointerType: 'mouse' });
    const menu = await screen.findByRole('menu');
    expect(within(menu).getAllByRole('menuitem').map((item) => item.textContent?.trim())).toEqual(['Disconnect Slack']);
  });

  it('keeps Disconnect in the More menu, behind a dialog', async () => {
    renderPage(connected(), { 'DELETE /v1/slack': () => new Response(null, { status: 204 }) });

    // bits-ui opens a menu on pointerdown.
    await fireEvent.pointerDown(await screen.findByRole('button', { name: 'More actions' }), { button: 0, pointerType: 'mouse' });
    const menu = await screen.findByRole('menu');
    expect(within(menu).getAllByRole('menuitem').map((item) => item.textContent?.trim())).toEqual(['Send test message', 'Reconnect', 'Disconnect Slack']);
    await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Disconnect Slack' }));
    const dialog = await screen.findByRole('dialog', { name: 'Disconnect Slack?' });

    expect(within(dialog).getByRole('button', { name: 'Keep it' })).toBeInTheDocument();
  });
});
