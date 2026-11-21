import type { AccountNotifications, NoticeRowState, UpdateAccountNotificationsRequest } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import { apiStub, user } from '../../../../test-support/fixtures.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/account/notifications') }, navigating: { to: null } }));
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto: vi.fn(async () => {}) }));

const row = (kind: NoticeRowState['kind'], overrides: Partial<NoticeRowState> = {}): NoticeRowState => ({
  kind,
  toasts: [],
  email: 'Instantly',
  emailDefault: 'Instantly',
  slack: null,
  scoped: false,
  ...overrides,
});

const grid = (overrides: Partial<AccountNotifications> = {}): AccountNotifications => ({
  scope: 'MyJobs',
  pauseToasts: false,
  closeToasts: true,
  toastsOff: ['Notes'],
  slackAvailable: true,
  slackLinked: false,
  rows: [
    row('Mentions', { toasts: ['Mentions'], slack: true }),
    row('Replies', { toasts: ['Replies'] }),
    row('Interviews'),
    row('Tasks', { toasts: ['Tasks'], email: 'Digest', emailDefault: 'Digest', slack: true }),
    row('NewApplications', { toasts: ['NewCandidates'], email: 'Digest', emailDefault: 'Digest', scoped: true }),
    row('TeammateChanges', { toasts: ['Moves', 'Notes', 'Tags', 'Emails', 'Scorecards', 'Templates'], email: 'Off', emailDefault: 'Off', scoped: true }),
    row('VerificationSuggested', { email: 'Off', emailDefault: 'Off', slack: false }),
  ],
  ...overrides,
});

function renderPage(routes: Parameters<typeof apiStub>[0]) {
  const fetchMock = vi.fn(apiStub(routes));
  vi.stubGlobal('fetch', fetchMock);
  render(WithQueryClient, { props: { component: Page, props: {} } });
  return fetchMock;
}

const table = async () => screen.findByRole('table');

describe('Your account > Notifications', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('shows a row per kind with its email choice, and a dash where nothing toasts', async () => {
    renderPage({ 'GET /v1/account/notifications': () => Response.json(grid()) });

    const grid_ = await table();
    expect(within(grid_).getByRole('combobox', { name: 'Mentions: email' })).toHaveValue('Instantly');
    expect(within(grid_).getByRole('combobox', { name: 'Tasks assigned to you: email' })).toHaveValue('Digest');
    expect(within(grid_).getByRole('combobox', { name: "Teammates' moves and notes: email" })).toHaveValue('Off');
    // Interviews has no toast yet, so its In the app cell is a dash with words for a screen reader, not a disabled switch (A3).
    expect(within(grid_).queryByRole('switch', { name: 'Interviews booked or changed: in the app' })).toBeNull();
    expect(within(grid_).getAllByText('No toast for this').length).toBeGreaterThan(0);
  });

  it('turns a row of toasts on only when every kind in it is on', async () => {
    renderPage({ 'GET /v1/account/notifications': () => Response.json(grid()) });

    const grid_ = await table();
    // Notes is off, so the row that covers it is off; Mentions is on.
    expect(within(grid_).getByRole('switch', { name: "Teammates' moves and notes: in the app" })).toHaveAttribute('aria-checked', 'false');
    expect(within(grid_).getByRole('switch', { name: 'Mentions: in the app' })).toHaveAttribute('aria-checked', 'true');
  });

  it('hides the Slack column until the person links Slack, and says where to link it', async () => {
    renderPage({ 'GET /v1/account/notifications': () => Response.json(grid()) });

    const grid_ = await table();
    expect(within(grid_).queryByRole('columnheader', { name: 'Slack' })).toBeNull();
    expect(screen.getByRole('link', { name: 'Connected accounts' })).toHaveAttribute('href', '/account/connected');
  });

  it('shows Slack once linked, with a dash on rows that have no Slack message', async () => {
    renderPage({ 'GET /v1/account/notifications': () => Response.json(grid({ slackLinked: true })) });

    const grid_ = await table();
    expect(within(grid_).getByRole('columnheader', { name: 'Slack' })).toBeInTheDocument();
    expect(within(grid_).getByRole('switch', { name: 'Mentions: Slack' })).toHaveAttribute('aria-checked', 'true');
    expect(within(grid_).queryByRole('switch', { name: 'Candidate replies: Slack' })).toBeNull();
  });

  it('saves the whole grid in one request', async () => {
    let body: UpdateAccountNotificationsRequest | undefined;
    renderPage({
      'GET /v1/account/notifications': () => Response.json(grid()),
      'PUT /v1/account/notifications': (init) => {
        body = JSON.parse(String(init?.body));
        return Response.json(grid());
      },
    });

    const grid_ = await table();
    await fireEvent.change(within(grid_).getByRole('combobox', { name: 'Mentions: email' }), { target: { value: 'Digest' } });
    await fireEvent.click(within(grid_).getByRole('switch', { name: "Teammates' moves and notes: in the app" }));
    await fireEvent.click(screen.getByRole('switch', { name: 'Pause toasts' }));
    await fireEvent.click(screen.getByRole('radio', { name: 'Everything I can see' }));
    await fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(body).toBeDefined());
    expect(body!.scope).toBe('Everything');
    expect(body!.pauseToasts).toBe(true);
    expect(body!.rows.find((entry) => entry.kind === 'Mentions')?.email).toBe('Digest');
    // Turning the row on turns every kind in it on, Notes included.
    expect(body!.toastsOff).toEqual([]);
    expect(await screen.findByText('Saved.')).toBeInTheDocument();
  });

  it('opens the kinds inside Teammates\' moves and notes one by one', async () => {
    renderPage({ 'GET /v1/account/notifications': () => Response.json(grid()) });

    const grid_ = await table();
    await fireEvent.click(within(grid_).getByRole('button', { name: 'Choose' }));
    expect(within(grid_).getByRole('switch', { name: 'Notes' })).toHaveAttribute('aria-checked', 'false');
    expect(within(grid_).getByRole('switch', { name: 'Moves' })).toHaveAttribute('aria-checked', 'true');
  });

  it('has no accessibility violations', async () => {
    renderPage({ 'GET /v1/account/notifications': () => Response.json(grid({ slackLinked: true })) });
    await table();
    await expectNoAxeViolations(document.body);
  });
});
