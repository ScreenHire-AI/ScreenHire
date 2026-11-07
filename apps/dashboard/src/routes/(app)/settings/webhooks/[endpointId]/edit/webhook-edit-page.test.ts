import type { WebhookEndpointEntry } from '@screenhire/api-client';
import { render, screen } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../../../test-support/axe.js';
import { apiStub, user } from '../../../../../../test-support/fixtures.js';
import WithQueryClient from '../../../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/settings/webhooks/e1/edit') }, navigating: { to: null } }));

const endpointId = 'e1';

const endpoint = (overrides: Partial<WebhookEndpointEntry> = {}): WebhookEndpointEntry => ({
  id: endpointId,
  url: 'https://hooks.example.com/applicants',
  description: 'Applicant sync',
  events: ['application.submitted', 'job.stage_changed'],
  enabled: true,
  consecutiveFailures: 0,
  disabledAt: null,
  disabledReason: null,
  createdAt: '2026-10-01T09:00:00Z',
  updatedAt: '2026-10-01T09:00:00Z',
  secretRotatedAt: '2026-10-01T09:00:00Z',
  lastUpdated: { at: '2026-10-01T09:00:00Z', by: 'Member', userId: 'u1', created: true },
  ...overrides,
});

afterEach(() => vi.unstubAllGlobals());

function renderPage(entry: WebhookEndpointEntry) {
  vi.stubGlobal('fetch', vi.fn(apiStub({ [`GET /v1/webhooks/${endpointId}`]: () => Response.json(entry) })));
  render(WithQueryClient, { props: { component: Page, props: { params: { endpointId } } } });
}

describe('edit webhook endpoint page', () => {
  it('fills the form with the endpoint, and says nothing about its state while it is on', async () => {
    renderPage(endpoint());

    expect(await screen.findByDisplayValue('https://hooks.example.com/applicants')).toBeInTheDocument();
    expect(screen.queryByText(/This endpoint is off/)).not.toBeInTheDocument();
  });

  it('says that saving does not turn an endpoint on while it is off, and links to where it is turned on', async () => {
    renderPage(endpoint({ enabled: false, disabledReason: 'Ten deliveries in a row failed.', disabledAt: '2026-10-19T09:00:00Z' }));

    expect(await screen.findByText('This endpoint is off. Saving does not turn it on.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Turn it on there' })).toHaveAttribute('href', `/settings/webhooks/${endpointId}`);
    await expectNoAxeViolations();
  });
});
