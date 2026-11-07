import type { ApiKeySummary } from '@screenhire/api-client';
import { fireEvent, render, screen, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import { apiStub, user } from '../../../../test-support/fixtures.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/settings/integrations') }, navigating: { to: null } }));

afterEach(() => vi.unstubAllGlobals());

const key = (overrides: Partial<ApiKeySummary> = {}): ApiKeySummary => ({
  id: 'k1',
  name: 'Payroll sync',
  prefix: 'sh_k1',
  scopes: ['Read'],
  permitsPerMinute: null,
  createdAt: '2026-10-01T09:00:00Z',
  lastUsedAt: null,
  revokedAt: null,
  createdByUserId: null,
  ...overrides,
});

function renderPage(keys: ApiKeySummary[] = [], endpoints: unknown[] = []) {
  vi.stubGlobal(
    'fetch',
    vi.fn(
      apiStub({
        'GET /v1/organization/api-keys': () => Response.json(keys),
        'GET /v1/webhooks': () => Response.json(endpoints),
      }),
    ),
  );
  render(WithQueryClient, { props: { component: Page, props: {} } });
}

const rows = () => within(screen.getByRole('list', { name: 'Integrations' })).getAllByRole('listitem');

describe('integrations directory', () => {
  it('lists each integration the person may open as a row linking to its page, with what it does', async () => {
    renderPage();

    expect(screen.getByRole('heading', { level: 1, name: 'Integrations' }).closest('[data-slot="header-band"]')).toHaveTextContent('3');
    expect(rows().map((row) => within(row).getByRole('link').textContent?.trim())).toEqual(['MCP server', 'Webhooks', 'API keys']);
    expect(screen.getByRole('link', { name: 'MCP server' })).toHaveAttribute('href', '/settings/mcp-server');
    expect(screen.getByRole('link', { name: 'Webhooks' })).toHaveAttribute('href', '/settings/webhooks');
    expect(screen.getByRole('link', { name: 'API keys' })).toHaveAttribute('href', '/settings/api-keys');
    expect(rows()[1]).toHaveTextContent('Send hiring events to your own systems as they happen.');
    // Slack is a Slack app the installation may not have (A3), and the shell has not heard that it does.
    expect(screen.queryByRole('link', { name: 'Slack' })).toBeNull();
    await expectNoAxeViolations();
  });

  it('says whether each is connected, from its own data, and says nothing where nothing can tell', async () => {
    renderPage([key(), key({ id: 'k2', revokedAt: '2026-10-10T09:00:00Z' })], []);

    await within(rows()[2]!).findByText('Connected');
    expect(within(rows()[1]!).getByText('Not connected')).toBeInTheDocument();
    // The MCP server signs in with an API key; nothing says whether an assistant uses it.
    expect(rows()[0]).not.toHaveTextContent(/connected/i);
  });

  it('counts a webhook endpoint as connected, and revoked keys alone as not', async () => {
    renderPage([key({ revokedAt: '2026-10-10T09:00:00Z' })], [{ id: 'w1', url: 'https://example.com/hook', description: null, events: [], enabled: true }]);

    await within(rows()[1]!).findByText('Connected');
    expect(within(rows()[2]!).getByText('Not connected')).toBeInTheDocument();
  });

  it('counts a webhook endpoint that is turned off as not connected, since it delivers nothing', async () => {
    renderPage([], [{ id: 'w1', url: 'https://example.com/hook', description: null, events: [], enabled: false }]);

    await within(rows()[1]!).findByText('Not connected');
  });

  it('filters by kind, with a count on each choice, and shows no search box for a short list', async () => {
    renderPage();
    const kinds = screen.getByRole('group', { name: 'Kind' });

    expect(within(kinds).getAllByRole('button').map((button) => button.textContent?.replace(/\s+/g, ' ').trim())).toEqual([
      'All',
      'AI assistants 1',
      'Developer 2',
    ]);
    expect(screen.queryByRole('searchbox')).toBeNull();

    await fireEvent.click(within(kinds).getByRole('button', { name: /^Developer/ }));

    expect(rows().map((row) => within(row).getByRole('link').textContent?.trim())).toEqual(['Webhooks', 'API keys']);
    expect(within(kinds).getByRole('button', { name: /^Developer/ })).toHaveAttribute('aria-pressed', 'true');
  });
});
