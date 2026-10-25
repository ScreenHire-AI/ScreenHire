import type { WebhookEndpointEntry } from '@screenhire/api-client';
import { render, screen, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import { apiStub, user } from '../../../../test-support/fixtures.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/settings/webhooks') }, navigating: { to: null } }));

const endpoint = (id: string, overrides: Partial<WebhookEndpointEntry> = {}): WebhookEndpointEntry => ({
  id,
  url: `https://hooks.example.com/${id}`,
  description: null,
  events: ['application.submitted'],
  enabled: true,
  consecutiveFailures: 0,
  disabledAt: null,
  disabledReason: null,
  createdAt: '2026-10-01T09:00:00Z',
  updatedAt: '2026-10-01T09:00:00Z',
  secretRotatedAt: '2026-10-01T09:00:00Z',
  ...overrides,
});

function renderPage(endpoints: WebhookEndpointEntry[] | Response) {
  vi.stubGlobal(
    'fetch',
    vi.fn(apiStub({ 'GET /v1/webhooks': () => (endpoints instanceof Response ? endpoints : Response.json(endpoints)) })),
  );
  render(WithQueryClient, { props: { component: Page, props: {} } });
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('webhooks page', () => {
  it('lists each endpoint by its description, or its address when it has none, linked to the endpoint', async () => {
    renderPage([
      endpoint('e1', { description: 'Applicant sync', events: ['application.submitted', 'application.moved'] }),
      endpoint('e2', { url: 'https://hooks.example.com/plain' }),
    ]);

    const described = (await screen.findAllByRole('link', { name: 'Applicant sync' }))[0]!;
    expect(described).toHaveAttribute('href', '/settings/webhooks/e1');
    expect(screen.getAllByRole('link', { name: 'https://hooks.example.com/plain' })[0]).toHaveAttribute('href', '/settings/webhooks/e2');
    expect(screen.getAllByText('2 events').length).toBeGreaterThan(0);
    expect(screen.getAllByText('1 event').length).toBeGreaterThan(0);
    await expectNoAxeViolations();
  });

  it('labels an endpoint that is on, one a person turned off, and one the worker turned off after failures', async () => {
    renderPage([
      endpoint('e1'),
      endpoint('e2', { enabled: false }),
      endpoint('e3', { enabled: false, disabledReason: 'Ten deliveries in a row failed.' }),
    ]);

    await screen.findAllByText('Delivering');
    expect(screen.getAllByText('Off').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Turned off').length).toBeGreaterThan(0);
  });

  it('offers to add the first endpoint when there are none', async () => {
    renderPage([]);

    expect(await screen.findByText('No webhooks yet')).toBeInTheDocument();
    // The empty state offers the one action too, beside the header band's (E1).
    expect(within(screen.getByRole('region', { name: 'Endpoints' })).getByRole('link', { name: 'Add endpoint' })).toHaveAttribute(
      'href',
      '/settings/webhooks/new',
    );
  });

  it('says why the endpoints could not load', async () => {
    renderPage(Response.json({ detail: 'Try again later.' }, { status: 500 }));

    expect(await screen.findByText('Try again later.')).toBeInTheDocument();
  });
});
