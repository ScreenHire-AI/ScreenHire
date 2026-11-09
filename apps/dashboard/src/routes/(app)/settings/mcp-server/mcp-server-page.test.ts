import type { OAuthConnectionSummary } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import { apiStub, user } from '../../../../test-support/fixtures.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/settings/mcp-server') }, navigating: { to: null } }));

afterEach(() => vi.unstubAllGlobals());

const connection = (id: string, clientName: string, overrides: Partial<OAuthConnectionSummary> = {}): OAuthConnectionSummary => ({
  id,
  clientName,
  connectedByUserId: user.id,
  connectedByName: 'Avery Quinn',
  scopes: ['Read'],
  createdAt: '2026-10-01T09:00:00Z',
  lastUsedAt: null,
  revokedAt: null,
  ...overrides,
});

const acme = connection('c1', 'Acme Assistant', { scopes: ['Read', 'Write', 'VerificationRead'], lastUsedAt: '2026-10-05T09:00:00Z' });
const old = connection('c2', 'Old Helper', { revokedAt: '2026-10-10T09:00:00Z' });

function renderPage(connections: OAuthConnectionSummary[], routes: Parameters<typeof apiStub>[0] = {}) {
  const fetchMock = vi.fn(
    apiStub({
      'GET /v1/meta': () => Response.json({ name: 'ScreenHire', version: '1.0.0', environment: 'Production', apiOrigin: 'https://api.example.com' }),
      'GET /v1/organization/oauth-connections': () => Response.json(connections),
      ...routes,
    }),
  );
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

describe('MCP server page', () => {
  it('lists every tool, with the two verification tools apart as the ones that need the scope', async () => {
    renderPage([]);

    const tools = await screen.findByRole('list', { name: 'Tools' });
    const verification = screen.getByRole('list', { name: 'Also with Verification read' });

    expect(within(tools).getAllByRole('listitem')).toHaveLength(10);
    expect(within(tools).getByText('Read the hiring report')).toBeInTheDocument();
    expect(within(tools).getByText('List saved candidates')).toBeInTheDocument();
    expect(within(tools).queryByText(/verification/i)).not.toBeInTheDocument();
    expect(within(verification).getAllByRole('listitem')).toHaveLength(2);
    // Each list says how many it holds, in the count badge after its label (T4).
    expect(screen.getByText('Tools').closest('dt')).toHaveTextContent('Tools10');
    expect(screen.getByText('Also with Verification read').closest('dt')).toHaveTextContent('Also with Verification read2');
    await expectNoAxeViolations();
  });

  it('keeps the API key instructions for a client that signs in with a key', async () => {
    renderPage([]);
    const panel = await screen.findByRole('region', { name: 'Connect a client' });

    expect(await within(panel).findByText('https://api.example.com/v1/mcp')).toBeInTheDocument();
    expect(within(panel).getByText('Authorization: Bearer <API key>')).toBeInTheDocument();
    expect(within(panel).getByRole('link', { name: 'Create a key' })).toHaveAttribute('href', '/settings/api-keys');
  });

  it('says what happens before anything is copied, until an assistant is connected (W9)', async () => {
    renderPage([]);
    const steps = await screen.findByRole('region', { name: 'How it works' });

    const list = within(steps).getByRole('list', { name: 'Steps to connect an assistant' });
    expect(within(list).getAllByRole('listitem').map((item) => item.textContent?.trim())).toEqual([
      '1 Copy the server URL below.',
      '2 In your AI assistant, add a custom connector and paste the URL.',
      '3 Sign in to ScreenHire when it asks, and choose what the assistant may do.',
    ]);
    await expectNoAxeViolations();
  });

  it('drops the steps once an assistant is connected (W10)', async () => {
    renderPage([acme]);

    expect((await screen.findAllByText('Acme Assistant')).length).toBeGreaterThan(0);
    expect(screen.queryByRole('region', { name: 'How it works' })).toBeNull();
  });

  describe('connected assistants', () => {
    it('lists each assistant with its access, who connected it and its status, and passes axe', async () => {
      renderPage([acme, old]);
      const panel = await screen.findByRole('region', { name: 'Connected assistants' });

      expect((await within(panel).findAllByText('Acme Assistant')).length).toBeGreaterThan(0);
      expect(within(panel).getAllByText('Read and write, with Verification read').length).toBeGreaterThan(0);
      expect(within(panel).getAllByText('Read only').length).toBeGreaterThan(0);
      expect(within(panel).getAllByText('Avery Quinn').length).toBeGreaterThan(0);
      expect(within(panel).getAllByText('Active').length).toBeGreaterThan(0);
      expect(within(panel).getAllByText('Revoked').length).toBeGreaterThan(0);
      expect(within(panel).getByRole('columnheader', { name: /Assistant/ })).toBeInTheDocument();
      expect(within(panel).getByRole('columnheader', { name: /Connected by/ })).toBeInTheDocument();
      // A revoked assistant has no menu.
      expect(screen.queryByRole('button', { name: 'Actions for Old Helper' })).not.toBeInTheDocument();
      await expectNoAxeViolations();
    });

    it('says so when none is connected', async () => {
      renderPage([]);

      expect(await screen.findByText('No assistants connected')).toBeInTheDocument();
    });

    it('explains a failed load and offers to try again', async () => {
      renderPage([], { 'GET /v1/organization/oauth-connections': () => new Response(null, { status: 500 }) });
      const panel = await screen.findByRole('region', { name: 'Connected assistants' });

      expect(await within(panel).findByRole('alert')).toHaveTextContent('Could not load the connected assistants');
      expect(within(panel).getByRole('button', { name: 'Try again' })).toBeInTheDocument();
    });

    it('disconnects an assistant through the menu and a confirmation, then reloads the list', async () => {
      let revoked = false;
      const fetchMock = renderPage([acme], {
        'GET /v1/organization/oauth-connections': () => Response.json(revoked ? [{ ...acme, revokedAt: '2026-10-23T09:00:00Z' }] : [acme]),
        'POST /v1/organization/oauth-connections/c1/revoke': () => {
          revoked = true;
          return new Response(null, { status: 204 });
        },
      });
      const menu = await openMenu('Actions for Acme Assistant');
      await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Disconnect assistant' }));
      const dialog = await screen.findByRole('dialog', { name: 'Disconnect Acme Assistant?' });

      await fireEvent.click(within(dialog).getByRole('button', { name: 'Disconnect assistant' }));

      await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/v1/organization/oauth-connections/c1/revoke', expect.objectContaining({ method: 'POST' })));
      await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
      expect(await screen.findAllByText('Revoked')).not.toHaveLength(0);
      expect(screen.getByText('Acme Assistant was disconnected.')).toBeInTheDocument();
    });

    it('disconnects nothing when the person backs out', async () => {
      const fetchMock = renderPage([acme]);
      const menu = await openMenu('Actions for Acme Assistant');
      await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Disconnect assistant' }));
      const dialog = await screen.findByRole('dialog', { name: 'Disconnect Acme Assistant?' });

      await fireEvent.click(within(dialog).getByRole('button', { name: 'Keep it' }));

      await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
      expect(fetchMock.mock.calls.filter(([, init]) => init?.method === 'POST')).toEqual([]);
    });
  });
});
