import type { ApiKeyScope, ApiKeySummary } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import { apiStub, user } from '../../../../test-support/fixtures.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/settings/api-keys') }, navigating: { to: null } }));
// Superforms needs the rest of the module (beforeNavigate).
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto: vi.fn(async () => {}) }));

const key = (id: string, name: string, overrides: Partial<ApiKeySummary> = {}): ApiKeySummary => ({
  id,
  name,
  prefix: `sh_${id}`,
  scopes: ['Read'],
  permitsPerMinute: null,
  createdAt: '2026-10-01T09:00:00Z',
  lastUsedAt: null,
  revokedAt: null,
  ...overrides,
});

const syncKey = key('k1', 'Payroll sync', { scopes: ['Read', 'Write'] });
const oldKey = key('k2', 'Old reporting job', { revokedAt: '2026-10-10T09:00:00Z' });

// A made-up token, long enough to look like one.
const token = 'shk_live_0123456789abcdef0123456789abcdef';

function renderPage(keys: ApiKeySummary[], routes: Parameters<typeof apiStub>[0] = {}) {
  const fetchMock = vi.fn(apiStub({ 'GET /v1/organization/api-keys': () => Response.json(keys), ...routes }));
  vi.stubGlobal('fetch', fetchMock);
  render(WithQueryClient, { props: { component: Page, props: {} } });
  return fetchMock;
}

// bits-ui opens a menu on pointerdown. Rows render in a phone list and a table (no CSS in jsdom), so a row has two buttons.
async function openMenu(name: string) {
  const [trigger] = await screen.findAllByRole('button', { name });
  await fireEvent.pointerDown(trigger!, { button: 0, pointerType: 'mouse' });
  return screen.findByRole('menu');
}

async function createKey(name: string, access: 'Read only' | 'Read and write') {
  await fireEvent.click(await screen.findByRole('button', { name: 'Create key' }));
  const dialog = await screen.findByRole('dialog', { name: 'Create an API key' });
  await fireEvent.input(within(dialog).getByLabelText('Name'), { target: { value: name } });
  await fireEvent.click(within(dialog).getByLabelText(access));
  await fireEvent.click(within(dialog).getByRole('button', { name: 'Create key' }));
  return dialog;
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('API keys page', () => {
  it('lists the keys with their access and shows a revoked key as Revoked', async () => {
    renderPage([syncKey, oldKey]);

    expect((await screen.findAllByText('Payroll sync')).length).toBeGreaterThan(0);
    expect(screen.getAllByText('Read and write').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Active').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Revoked').length).toBeGreaterThan(0);
    await expectNoAxeViolations();
  });

  it('offers to create the first key when there are none', async () => {
    renderPage([]);

    expect(await screen.findByText('No API keys yet')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Create key' })).toBeInTheDocument();
  });

  it('gives a revoked key no actions, and an active key one', async () => {
    renderPage([syncKey, oldKey]);
    await screen.findAllByText('Payroll sync');

    expect(screen.getAllByRole('button', { name: 'Actions for Payroll sync' }).length).toBeGreaterThan(0);
    expect(screen.queryAllByRole('button', { name: 'Actions for Old reporting job' })).toHaveLength(0);
  });

  it.each<[string, 'Read only' | 'Read and write', ApiKeyScope[]]>([
    ['Dashboard export', 'Read only', ['Read']],
    ['Applicant sync', 'Read and write', ['Read', 'Write']],
  ])('creates %s as %s with the scopes %j', async (name, access, scopes) => {
    const bodies: unknown[] = [];
    renderPage([], {
      'POST /v1/organization/api-keys': (init) => {
        bodies.push(JSON.parse(String(init?.body)));
        return Response.json({ key: key('k9', name, { scopes }), token }, { status: 201 });
      },
    });

    await createKey(name, access);

    await waitFor(() => expect(bodies).toEqual([{ name, scopes }]));
  });

  it('shows the new token once, with Copy, and forgets it after Done', async () => {
    const writeText = vi.fn(async () => {});
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    renderPage([], {
      'POST /v1/organization/api-keys': () => Response.json({ key: key('k9', 'Dashboard export'), token }, { status: 201 }),
    });

    await createKey('Dashboard export', 'Read only');

    const dialog = await screen.findByRole('dialog', { name: 'Your new API key' });
    expect(within(dialog).getByDisplayValue(token)).toHaveAttribute('readonly');
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Copy' }));
    expect(writeText).toHaveBeenCalledWith(token);
    expect(await within(dialog).findByRole('button', { name: 'Copied' })).toBeInTheDocument();

    await fireEvent.click(within(dialog).getByRole('button', { name: 'Done' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(screen.queryByDisplayValue(token)).not.toBeInTheDocument();
    expect(document.body.textContent).not.toContain(token);
  });

  it('asks for a name before sending anything', async () => {
    const fetchMock = renderPage([]);

    const dialog = await createKey('   ', 'Read only');

    expect(await within(dialog).findByText('Give the key a name.')).toBeInTheDocument();
    expect(fetchMock.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(0);
  });

  it('keeps the create dialog open and says why when the API refuses', async () => {
    renderPage([], { 'POST /v1/organization/api-keys': () => Response.json({ detail: 'Only owners and admins create keys.' }, { status: 403 }) });

    const dialog = await createKey('Dashboard export', 'Read only');

    expect(await within(dialog).findByText('Only owners and admins create keys.')).toBeInTheDocument();
    expect(screen.queryByDisplayValue(token)).not.toBeInTheDocument();
  });

  it('revokes an active key through the menu and a confirmation, with a POST', async () => {
    const fetchMock = renderPage([syncKey, oldKey], {
      'POST /v1/organization/api-keys/k1/revoke': () => new Response(null, { status: 204 }),
    });
    const menu = await openMenu('Actions for Payroll sync');

    await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Revoke key' }));
    const dialog = await screen.findByRole('dialog', { name: 'Revoke Payroll sync?' });
    expect(fetchMock.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(0);

    await fireEvent.click(within(dialog).getByRole('button', { name: 'Revoke key' }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/v1/organization/api-keys/k1/revoke', expect.objectContaining({ method: 'POST' })));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });

  it('revokes nothing when the person keeps the key', async () => {
    const fetchMock = renderPage([syncKey]);
    const menu = await openMenu('Actions for Payroll sync');

    await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Revoke key' }));
    const dialog = await screen.findByRole('dialog', { name: 'Revoke Payroll sync?' });
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Keep it' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(fetchMock.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(0);
  });

  it('keeps the confirmation open and says why when revoking fails', async () => {
    renderPage([syncKey], { 'POST /v1/organization/api-keys/k1/revoke': () => Response.json({ detail: 'The key was already revoked.' }, { status: 409 }) });
    const menu = await openMenu('Actions for Payroll sync');

    await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Revoke key' }));
    const dialog = await screen.findByRole('dialog');
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Revoke key' }));

    await waitFor(() => expect(within(dialog).getByRole('alert')).toHaveTextContent('The key was already revoked.'));
    expect(dialog).toBeInTheDocument();
  });
});
